using demo;
using JetBrains.Collections.Viewable;
using JetBrains.Lifetimes;
using JetBrains.Rd;
using JetBrains.Rd.Base;
using JetBrains.Rd.Impl;
using NUnit.Framework;
using Test.RdCross.Util;

namespace Test.RdCross
{
    public class CrossTestCsClientAllEntities : CrossTestCsClientBase
    {
        public static void Main(string[] args)
        {
            new CrossTestCsClientAllEntities().Run(args);
        }

        private void AdviseAll(Lifetime lifetime, DemoModel model, ExtModel extModel)
        {
            model.Boolean_property.Advise(lifetime,
                it => { Printer.PrintIfRemoteChange(model.Boolean_property, "Boolean_property", it); });

            model.Boolean_array.Advise(lifetime,
                it => { Printer.PrintIfRemoteChange(model.Boolean_array, "Boolean_array", it); });

            model.Scalar.Advise(lifetime, it => { Printer.PrintIfRemoteChange(model.Scalar, "Scalar", it); });

            model.Ubyte.Advise(lifetime, it => { Printer.PrintIfRemoteChange(model.Ubyte, "Ubyte", it); });

            model.Ubyte_array.Advise(lifetime,
                it => { Printer.PrintIfRemoteChange(model.Ubyte_array, "Ubyte_array", it); });

            model.List.Advise(lifetime, e => { Printer.PrintIfRemoteChange(model.List, "List", e); });

            model.Set.Advise(lifetime, e => { Printer.PrintIfRemoteChange(model.Set, "Set", e); });

            model.MapLongToString.Advise(lifetime,
                e => { Printer.PrintIfRemoteChange(model.MapLongToString, "MapLongToString", e); });

            /*model.Callback.Set(s =>
            {
                Printer.Print("RdTask:");
                Printer.Print(s);

                return 50;
            });*/

            model.Interned_string.Advise(lifetime,
                it => { Printer.PrintIfRemoteChange(model.Interned_string, "Interned_string", it); });

            model.Polymorphic.Advise(lifetime,
                it => { Printer.PrintIfRemoteChange(model.Polymorphic, "Polymorphic", it); });

            model.Enum.Advise(lifetime, it =>
            {
                Printer.PrintIfRemoteChange(model.Enum, "Enum", it);
                if (!model.Enum.IsLocalChange())
                {
                    Finished = true;
                }
            });

            extModel.Checker.Advise(lifetime, () =>
            {
                Printer.Print("ExtModel:Checker:");
                "Unit".PrintEx(Printer);
            });
        }

        static void FireAll(DemoModel model, ExtModel extModel)
        {
            model.Boolean_property.Set(false);

            model.Boolean_array.Set(new[] {false, true, false});

            var scalar = new MyScalar(false,
                50,
                32000,
                1000000000,
                -2000000000000000000,
                3.14f,
                -123456789.012345678,
                byte.MaxValue - 1,
                ushort.MaxValue - 1,
                uint.MaxValue - 1,
                ulong.MaxValue - 1,
                MyEnum.net
            );
            model.Scalar.Set(scalar);

            model.Set.Add(50);

            model.MapLongToString.Add(50, "C#");

            var valA = "C#";
            var valB = "protocol";

            // var res = model.get_call().sync(L'c');

            model.Interned_string.Set(valA);
            model.Interned_string.Set(valA);
            model.Interned_string.Set(valB);
            model.Interned_string.Set(valB);
            model.Interned_string.Set(valA);

            var derived = new Derived("C# instance");
            model.Polymorphic.Set(derived);
            model.Enum.Value = MyEnum.net;
            extModel.Checker.Fire();
        }

        public override void Run(string[] args)
        {
            Before(args);

            CheckConstants();

            SingleThreadScheduler.RunOnSeparateThread(SocketLifetime, "Worker", scheduler =>
            {
                var client = new SocketWire.Client(ModelLifetime, scheduler, Port, "DemoServer");
                var serializers = new Serializers();
                Protocol = new Protocol("Server", serializers, new Identities(IdKind.Server), scheduler,
                    client, SocketLifetime);
                scheduler.Queue(() =>
                {
                    var model = new DemoModel(ModelLifetime, Protocol);
                    var extModel = model.GetExtModel();

                    AdviseAll(ModelLifetime, model, extModel);
                    FireAll(model, extModel);
                });
            });

            After();
        }

        private static void CheckConstants()
        {
            Assert.True(DemoModel.const_toplevel);
            Assert.AreEqual(ConstUtil.const_enum, MyEnum.@default);
            Assert.AreEqual(ConstUtil.const_string, "const_string_value");
            Assert.AreEqual(Base.const_base, 'B');
        }
    }
}