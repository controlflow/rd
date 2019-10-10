package com.jetbrains.rd.framework.test.cases.sync

import com.jetbrains.rd.framework.*
import com.jetbrains.rd.framework.test.util.TestScheduler
import com.jetbrains.rd.util.Logger
import com.jetbrains.rd.util.addUnique
import com.jetbrains.rd.util.lifetime.Lifetime
import com.jetbrains.rd.util.lifetime.LifetimeDefinition
import com.jetbrains.rd.util.log.ErrorAccumulatorLoggerFactory
import com.jetbrains.rd.util.spinUntil
import org.junit.Test
import test.synchronization.Clazz
import test.synchronization.SyncModelRoot
import java.lang.management.ManagementFactory
import kotlin.test.AfterTest
import kotlin.test.BeforeTest

class TestTwoClients {

    private lateinit var lifetimeDef : LifetimeDefinition
    private val lifetime : Lifetime get() = lifetimeDef.lifetime

    lateinit var c0: SyncModelRoot
    lateinit var c1: SyncModelRoot
    lateinit var s0: SyncModelRoot
    lateinit var s1: SyncModelRoot

    private val isUnderDebug = ManagementFactory.getRuntimeMXBean().getInputArguments().any { it.contains("jdwp") }
    val timeout = if (isUnderDebug) 10_000L else 1000L

    fun wait(condition: () -> Boolean) {
        require (spinUntil(timeout, condition))
    }

    @BeforeTest
    fun setup() {
        lifetimeDef = LifetimeDefinition()
        Logger.set(lifetime, ErrorAccumulatorLoggerFactory)

        val sc = TestScheduler

        val wireFactory = SocketWire.ServerFactory(lifetime, sc, null, false)
        val port = wireFactory.localPort

        val sp = mutableListOf<Protocol>()
        var spIdx = 0
        wireFactory.view(lifetime) { lf, wire ->
            val protocol = Protocol("s[${spIdx++}]", Serializers(), Identities(IdKind.Server), sc, wire, lf)
            sp.addUnique(lf, protocol)
        }


        var cpIdx = 0
        val cpFunc = { Protocol("c[${cpIdx++}]", Serializers(), Identities(IdKind.Client), sc, SocketWire.Client(lifetime, sc, port), lifetime) }

        val cp = mutableListOf<Protocol>()
        cp.add(cpFunc())
        cp.add(cpFunc())

        wait { sp.size  == 2 }

        c0 = SyncModelRoot.create(lifetime, cp[0])
        c1 = SyncModelRoot.create(lifetime, cp[1])

        s0 = SyncModelRoot.create(lifetime, sp[0])
        s1 = SyncModelRoot.create(lifetime, sp[1])

        s0.synchronizeWith(lifetime, s1)
    }

    @AfterTest
    fun teardown() {
        lifetimeDef.terminate()
        ErrorAccumulatorLoggerFactory.throwAndClear()
    }

    @Test
    fun testNotnullableScalarProperty() {
        c0.aggregate.nonNullableScalarProperty.set("a")
        wait { c1.aggregate.nonNullableScalarProperty.valueOrNull == "a" }

        c1.aggregate.nonNullableScalarProperty.set("b")
        wait { c0.aggregate.nonNullableScalarProperty.valueOrNull == "b" }
    }


    @Test
    fun testNullableScalarProperty() {
        c0.aggregate.nullableScalarProperty.set("a")
        wait { c1.aggregate.nullableScalarProperty.value == "a" }

        c1.aggregate.nullableScalarProperty.set("b")
        wait { c0.aggregate.nullableScalarProperty.value == "b" }

        c1.aggregate.nullableScalarProperty.set(null)
        wait { c0.aggregate.nullableScalarProperty.value == null }
    }

    @Test
    fun testList() {
        c0.list.add(Clazz(1))
        wait { c1.list.size == 1 }
        assert(c1.list[0].f == 1)

        c0.list[0].p.value = 2
        wait { c1.list[0].p.value == 2 }
    }

}