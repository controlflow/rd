package com.jetbrains.rd.util.lifetime

import com.jetbrains.rd.util.AtomicInteger
import com.jetbrains.rd.util.AtomicReference
import com.jetbrains.rd.util.Logger
import com.jetbrains.rd.util.error

open class SequentialLifetimes(private val parentLifetime: Lifetime) {
    private val currentDef = AtomicReference(LifetimeDefinition.Terminated)

    // clearCount is needed to clear ParentLifetime from time to time, to avoid leakage of nested lifetimes
    private var clearCount: AtomicInteger?

    init {
        parentLifetime += { setNextLifetime(true) } //todo toRemove
        clearCount = if (parentLifetime.isEternal) null else AtomicInteger(0)
    }

    open fun next(): LifetimeDefinition {
        return setNextLifetime(false)
    }

    open fun terminateCurrent() {
        setNextLifetime(true)
    }

    val isTerminated: Boolean get() { //todo toRemove
        val current = currentDef.get()
        return current.isEternal || !current.isAlive
    }

    open fun defineNext(fNext: (LifetimeDefinition, Lifetime) -> Unit) {
        setNextLifetime(false) { ld ->
            try {
                ld.executeIfAlive { fNext(ld, ld.lifetime) }
            } catch (t: Throwable) {
                ld.terminate()
                throw t
            }
        }
    }

    /**
     * Atomically, assigns the new lifetime and terminates the old one.
     *
     * In case of a race condition, when current lifetime is overwritten, new lifetime is terminated.
     */
    protected fun setNextLifetime(useTerminated: Boolean, action: ((LifetimeDefinition) -> Unit)? = null): LifetimeDefinition {
        // Temporary lifetime definition that'll be used as a substitutor during current lifetime termination. We cannot
        // use Lifetime.Terminated here, because we need a distinct instance to use it in atomic operations later.
        val tempLifetimeDefinition = LifetimeDefinition()
        tempLifetimeDefinition.terminate()

        val old = currentDef.getAndSet(tempLifetimeDefinition)
        try {
            old.terminate(true)
        } catch (t: Throwable) {
            Logger.root.error(t)
        }

        val newDef = if (useTerminated) LifetimeDefinition.Terminated else parentLifetime.createNested()
        try {
            action?.invoke(newDef)
        } finally {
            if (!currentDef.compareAndSet(tempLifetimeDefinition, newDef)) {
                // Means someone else has already interrupted us and replaced the current value (a race condition).
                newDef.terminate(true)
            }
        }

        clearParentLifetime()

        return newDef
    }

    private fun clearParentLifetime() {
        val atomicCount = clearCount ?: return

        if (atomicCount.incrementAndGet() >= 1000) {

            while (true) {
                val oldValue = atomicCount.get()
                if (oldValue < 1000) return

                if (atomicCount.compareAndSet(oldValue, 0)) {
                    // !!! DIRTY HACK to prevent leakage of nested lifetime !!!
                    // todo need to rewrite the lifetime implementation as on dotnet side
                    (parentLifetime as LifetimeDefinition).clearObsoleteAttachedLifetimesIfThereAreMany()

                    return
                }
            }
        }
    }
}