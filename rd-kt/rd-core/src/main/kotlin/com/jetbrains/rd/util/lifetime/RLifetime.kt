//@file:JvmName("LifetimeKt")

package com.jetbrains.rd.util.lifetime

import com.jetbrains.rd.util.*
import com.jetbrains.rd.util.collections.CountingSet
import com.jetbrains.rd.util.lifetime.LifetimeStatus.*
import com.jetbrains.rd.util.reactive.IViewable
import com.jetbrains.rd.util.reactive.viewNotNull
import kotlin.math.min

enum class LifetimeStatus {
    Alive,
    Canceling,
    Terminating,
    Terminated
}


sealed class Lifetime {
    companion object {
        private val threadLocalExecutingBackingFiled : ThreadLocal<CountingSet<Lifetime>> = threadLocalWithInitial { CountingSet() }
        // !!! IMPORTANT !!! Don't use 'by ThreadLocal' to avoid slow reflection initialization
        internal val threadLocalExecuting get() = threadLocalExecutingBackingFiled.get()

        var waitForExecutingInTerminationTimeout = 500L //timeout for waiting executeIfAlive in termination

        val Eternal : Lifetime get() = LifetimeDefinition.eternal //some marker
        val Terminated get() = LifetimeDefinition.Terminated.lifetime

        inline fun <T> using(block : (Lifetime) -> T) : T{
            val def = LifetimeDefinition()
            try {
                return block(def.lifetime)
            } finally {
                def.terminate()
            }
        }
    }

    fun createNested() = LifetimeDefinition().also { attach(it) }

    fun createNested(atomicAction : (LifetimeDefinition) -> Unit) = createNested().also { nested ->
        attach(nested)
        try {
            nested.executeIfAlive { atomicAction(nested) }
        } catch (e: Exception) {
            nested.terminate()
            throw e
        }
    }

    inline fun <T> usingNested(action: (Lifetime) -> T): T {
        val nested = createNested()
        return try {
            action(nested.lifetime)
        } finally {
            nested.terminate()
        }
    }

    abstract val status : LifetimeStatus

    abstract fun <T : Any> executeIfAlive(action: () -> T) : T?

    abstract fun onTerminationIfAlive(action: () -> Unit): Boolean
    abstract fun onTerminationIfAlive(closeable: Closeable): Boolean

    abstract fun onTermination(action: () -> Unit)
    abstract fun onTermination(closeable: Closeable)

    abstract fun <T : Any> bracket(opening: () -> T, terminationAction: () -> Unit): T?
    abstract fun <T : Any> bracket2(opening: () -> T, terminationAction: (T) -> Unit): T?

    internal abstract fun attach(child: LifetimeDefinition)

    @Deprecated("Use isNotAlive")
    val isTerminated: Boolean get() = isNotAlive
}


class LifetimeDefinition : Lifetime() {
    val lifetime: Lifetime get() = this

    companion object {
        internal val eternal = LifetimeDefinition()
        private val log: Logger by lazy { getLogger<Lifetime>() }

        //State decomposition
        private val executingSlice = BitSlice.int(20)
        private val statusSlice = BitSlice.enum<LifetimeStatus>(executingSlice)
        private val mutexSlice = BitSlice.bool(statusSlice)
        private val logErrorAfterExecution = BitSlice.bool(mutexSlice)

        val Terminated: LifetimeDefinition = LifetimeDefinition()

        const val anonymousLifetimeId = "Anonymous"

        init {
            Terminated.terminate()
        }
    }


    //Fields
    private var state = AtomicInteger()
    private var resources: Array<Any?>? = arrayOfNulls(1)
    private var resCount = 0

    /**
     * Only possible [Alive] -> [Canceling] -> [Terminating] -> [Terminated]
     */
    override val status: LifetimeStatus get() = statusSlice[state]

    /**
     * You can optionally set this identification information to see logs with lifetime's id other than <see cref="AnonymousLifetimeId"/>
     */
    var id: Any? = null


    override fun <T : Any> executeIfAlive(action: () -> T): T? {
        //increase [executing] by 1
        while (true) {
            val s = state.get()
            if (statusSlice[s] != Alive)
                return null

            if (state.compareAndSet(s, s + 1))
                break
        }

        threadLocalExecuting.add(this@LifetimeDefinition, +1)
        try {

            return action()

        } finally {
            threadLocalExecuting.add(this@LifetimeDefinition, -1)
            state.decrementAndGet()

            if (logErrorAfterExecution[state]) {
                log.error { "ExecuteIfAlive after termination of $this took too much time (>${waitForExecutingInTerminationTimeout}ms)" }
            }
        }
    }


    private inline fun <T> underMutexIf(status: LifetimeStatus, action: () -> T): T? {
        //increase [executing] by 1
        while (true) {
            val s = state.get()
            if (statusSlice[s] > status)
                return null

            if (mutexSlice[s])
                continue

            if (state.compareAndSet(s, mutexSlice.updated(s, true)))
                break
        }


        try {

            return action()

        } finally {
            while (true) {
                val s = state.get()
                assert(mutexSlice[s])

                if (state.compareAndSet(s, mutexSlice.updated(s, false)))
                    break
            }
        }
    }

    private fun tryAdd(action: Any): Boolean {
        //we could add anything to Eternal lifetime and it'll never be executed
        if (lifetime === eternal)
            return true

        return underMutexIf(Canceling) {
            val localResources = resources
            require(localResources != null) { "$: `resources` can't be null under mutex while status < Terminating" }

            if (resCount == localResources.size) {
                var countAfterCleaning = 0
                for (i in 0 until resCount) {
                    //can't clear Canceling because TryAdd works in Canceling state
                    val resource = localResources[i]
                    if (resource is LifetimeDefinition && resource.status >= Terminating) {
                        localResources[i] = null
                    } else {
                        localResources[countAfterCleaning++] = resource
                    }
                }

                resCount = countAfterCleaning
                if (countAfterCleaning * 2 > localResources.size) {
                    val newArray =
                        arrayOfNulls<Any?>(countAfterCleaning * 2)  //must be more than 1, so it always should be room for one more resource
                    localResources.copyInto(newArray)
                    resources = newArray
                }
            }

            resources!![resCount++] = action
            true
        } ?: false
    }


    private fun incrementStatusIf(status: LifetimeStatus): Boolean {
        assert(this !== eternal) { "Trying to change eternal lifetime" }

        while (true) {
            val s = state.get()
            if (statusSlice[s] != status)
                return false

            val nextStatus = enumValues<LifetimeStatus>()[statusSlice[s].ordinal + 1]
            val newS = statusSlice.updated(s, nextStatus)

            if (state.compareAndSet(s, newS))
                return true
        }
    }


    private fun markCancelingRecursively() {
        assert(this !== eternal) { "Trying to terminate eternal lifetime" }

        if (!incrementStatusIf(Alive))
            return

        // Some other thread can already begin destructuring
        // Then children lifetimes become canceled in their termination

        // In fact here access to resources could be done without mutex because setting cancellation status of children is rather optimization than necessity
        val localResources = resources ?: return

        //Math.min is to ensure that even if some other thread increased myResCount, we don't get IndexOutOfBoundsException
        for (i in min(resCount, localResources.size) - 1 downTo 0) {
            (localResources[i] as? LifetimeDefinition)?.markCancelingRecursively();
        }
    }


    fun terminate(supportsTerminationUnderExecuting: Boolean = false): Boolean {
        if (isEternal || status > Canceling)
            return false



        if (!supportsTerminationUnderExecuting && threadLocalExecuting[this] > 0) {
            error("Can't terminate lifetime under `executeIfAlive` because termination doesn't support this. Use `terminate(true)`")
        }


        markCancelingRecursively()

        //wait for all executions finished
        if (!spinUntil(waitForExecutingInTerminationTimeout) { executingSlice[state] <= threadLocalExecuting[this] }) {
            log.warn {
                ("$this: can't wait for `ExecuteIfAlive` completed on other thread in $waitForExecutingInTerminationTimeout ms. Keep termination." + System.lineSeparator()
                    + "This may happen either because of the ExecuteIfAlive failed to complete in a timely manner. In the case there will be following error messages." + System.lineSeparator()
                    + "Or this might happen because of garbage collection or when the thread yielded execution in SpinWait.SpinOnce but did not receive execution back in a timely manner. If you are on JetBrains' Slack see the discussion https://jetbrains.slack.com/archives/CAZEUK2R0/p1606236742208100")
            }

            logErrorAfterExecution.atomicUpdate(state, true);
        }


        //Already terminated by someone else.
        if (!incrementStatusIf(Canceling))
            return false

        //Now status is 'Terminating' and we have to wait for all resource modifications to complete. No mutex acquire is possible beyond this point.
        spinUntil { !mutexSlice[state] }

        destruct(supportsTerminationUnderExecuting)

        return true
    }


    //assumed that we are already in Terminating state
    private fun destruct(supportsRecursion: Boolean) {
        assert(status == Terminating) { "Bad status for destructuring start: $status" }
        assert(!mutexSlice[state]) { "$this: mutex must be released in this point" }
        //no one can take mutex after this point

        val localResources = resources
        require(localResources != null) { "$this: `resources` can't be null on destructuring stage" }

        for (i in resCount - 1 downTo 0) {
            val resource = localResources[i] ?: break
            terminateResource(resource, supportsRecursion)
        }

        resources = null
        resCount = 0

        require(incrementStatusIf(Terminating)) { "Bad status for destructuring finish: $status" }
    }

    private fun terminateResource(resource: Any, supportsRecursion: Boolean) {
        try {
            when (resource) {
                is () -> Any? -> resource()

                is Closeable -> resource.close()

                is LifetimeDefinition -> resource.terminate(supportsRecursion)

                else -> log.error { "Unknown termination resource: $resource" }
            }
        } catch (e: Throwable) {
            log.error("$this: exception on termination of resource: $resource", e);
        }
    }


    override fun onTerminationIfAlive(action: () -> Unit) = tryAdd(action)
    override fun onTerminationIfAlive(closeable: Closeable) = tryAdd(closeable)

    override fun onTermination(action: () -> Unit) = onTermination(action as Any)
    override fun onTermination(closeable: Closeable) = onTermination(closeable as Any)

    private fun onTermination(obj: Any) {
        if (tryAdd(obj)) return

        terminateResource(obj, true)
        error("$this: can't add termination action if lifetime terminating or terminated (Status > Canceling); you can consider usage of `onTerminationIfAlive` ")
    }


    override fun attach(child: LifetimeDefinition) {
        require(!child.isEternal) { "Can't attach eternal lifetime" }

        if (child.isNotAlive)
            return

        if (!this.tryAdd(child))
            child.terminate()
    }


    override fun <T : Any> bracket(opening: () -> T, terminationAction: () -> Unit): T? {
        return executeIfAlive {
            val res = opening()

            if (!tryAdd(terminationAction)) {
                //terminated with `terminate(true)`
                terminationAction()
            }
            res
        }
    }

    override fun <T : Any> bracket2(opening: () -> T, terminationAction: (T) -> Unit): T? {
        return executeIfAlive {
            val res = opening()

            if (!tryAdd({ terminationAction(res) })) {
                //terminated with `terminate(true)`
                terminationAction(res)
            }
            res
        }
    }

    override fun toString() = "Lifetime `${id ?: anonymousLifetimeId}` [${status}, executing=${executingSlice[state]}, resources=$resCount]"
}

fun Lifetime.waitTermination() = spinUntil { status == Terminated }

fun Lifetime.throwIfNotAlive() { if (status != Alive) throw CancellationException() }
fun Lifetime.assertAlive() { assert(status == Alive) { "Not alive: $status" } }

val Lifetime.isAlive : Boolean get() = status == Alive
val Lifetime.isNotAlive : Boolean get() = status != Alive
val Lifetime.isEternal : Boolean get() = this === Lifetime.Eternal


val EternalLifetime get() = Lifetime.Eternal

operator fun Lifetime.plusAssign(action : () -> Unit) = onTermination(action)

fun Lifetime.intersect(lifetime: Lifetime): LifetimeDefinition {
    return LifetimeDefinition().also {
        this.attach(it)
        lifetime.attach(it)
    }
}


inline fun <T> Lifetime.view(viewable: IViewable<T>, crossinline handler: Lifetime.(T) -> Unit) {
    viewable.view(this) { lt, value -> lt.handler(value) }
}

inline fun <T:Any> Lifetime.viewNotNull(viewable: IViewable<T?>, crossinline handler: Lifetime.(T) -> Unit) {
    viewable.viewNotNull(this) { lt, value -> lt.handler(value) }
}
