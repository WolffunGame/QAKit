using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;

namespace QA.PerfKit.Tests
{
    public sealed class PerfLineBuilderTests
    {
        private static FrameSample Sample(float ft, float cpu, float rend, float gpu, int ram = 512, bool gc = false) =>
            new FrameSample { ftMs = ft, cpuMs = cpu, rendMs = rend, gpuMs = gpu, ramMb = ram, gc = gc };

        private static PerfContext ShopContext()
        {
            var context = new PerfContext();
            context.Set("screen", "Shop");
            context.Set("wave", "5");
            return context;
        }

        [Test]
        public void Drop_line_has_all_keys_in_order()
        {
            CultureInfo previous = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");   // decimal comma must not leak in
            try
            {
                var builder = new PerfLineBuilder();
                builder.Push(Sample(33.4f, 33.4f, 10f, 30.1f));
                builder.Push(Sample(34.0f, 34.0f, 10f, 31.2f));
                builder.Push(Sample(52.3f, 52.3f, 10f, 60.8f));

                string line = builder.Drop(1730264954285, Sample(98f, 61.2f, 20.1f, 88.4f, 512, gc: true), 33.3f,
                    "Level 12", ShopContext());

                StringAssert.IsMatch(
                    @"^DROP t=\d+ ft=\d+\.\d tgt=\d+\.\d efps=\d+ cpu=.* rend=.* gpu=.* ram=\d+ gc=[01] scene=\S+ ctx=\S* prev=\S+$",
                    line);
                Assert.AreEqual(
                    "DROP t=1730264954285 ft=98.0 tgt=33.3 efps=10 cpu=61.2 rend=20.1 gpu=88.4 ram=512 gc=1 " +
                    "scene=Level_12 ctx=screen:Shop;wave:5 prev=33.4/30.1,34.0/31.2,52.3/60.8",
                    line);

                // Empty scene / ctx / ring still give one token per key.
                string bare = new PerfLineBuilder().Drop(1, Sample(50f, -1f, -1f, -1f, 100), 16.7f, null, new PerfContext());
                Assert.AreEqual("DROP t=1 ft=50.0 tgt=16.7 efps=20 cpu=-1 rend=-1 gpu=-1 ram=100 gc=0 scene=- ctx= prev=-", bare);

                Assert.AreEqual("MARK t=1730264957000 name=Boss_Spawn", builder.Mark(1730264957000, "Boss Spawn"));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        [Test]
        public void Gpu_unavailable_is_minus_one()
        {
            string line = new PerfLineBuilder().Drop(1, Sample(98f, 61.2f, 20.1f, -1f), 33.3f, "Level_12", new PerfContext());

            StringAssert.Contains(" gpu=-1 ", line);
            StringAssert.DoesNotContain("gpu=-1.0", line);
            StringAssert.Contains(" cpu=61.2 ", line);
        }

        [Test]
        public void Rate_limit_allows_10_then_burst()
        {
            var builder = new PerfLineBuilder();
            int allowed = 0;
            for (int i = 0; i < 25; i++)
                if (builder.AllowDrop(5000 + i, 40f + i)) allowed++;

            Assert.AreEqual(10, allowed);

            // The burst covers one second from the first suppressed drop.
            Assert.IsFalse(builder.TakeBurst(5100, out _, out _));
            Assert.IsTrue(builder.TakeBurst(6010, out int n, out float worst));
            Assert.AreEqual(15, n);
            Assert.AreEqual(64f, worst, 0.001f);
            Assert.IsFalse(builder.TakeBurst(7100, out _, out _), "flushed once");

            // A second later the window has rolled: drops are allowed again.
            Assert.IsTrue(builder.AllowDrop(6100, 40f));

            Assert.AreEqual("DROPBURST t=1730264956000 n=37 worst=210.4", builder.Burst(1730264956000, 37, 210.4f));
        }

        [Test]
        public void Prev_ring_holds_last_three()
        {
            var builder = new PerfLineBuilder();
            for (int i = 1; i <= 5; i++) builder.Push(Sample(i, i, 0f, i * 10f));

            string line = builder.Drop(1, Sample(98f, 61.2f, 20.1f, 88.4f), 33.3f, "s", new PerfContext());

            Match prev = Regex.Match(line, @" prev=(\S+)$");
            Assert.IsTrue(prev.Success, line);
            Assert.AreEqual("3.0/30.0,4.0/40.0,5.0/50.0", prev.Groups[1].Value);
        }

        [Test]
        public void Heartbeat_line_format()
        {
            var builder = new PerfLineBuilder();
            string line = builder.Heartbeat(1730264955000, 29.8f, Sample(33.5f, 12.1f, 8.0f, 14.3f, 510), "Level 12",
                ShopContext());

            Assert.AreEqual(
                "HB t=1730264955000 fps=30 cpu=12.1 rend=8.0 gpu=14.3 ram=510 scene=Level_12 ctx=screen:Shop;wave:5",
                line);

            string noGpu = builder.Heartbeat(2, 60f, Sample(16f, -1f, -1f, -1f, 1), "a", new PerfContext());
            Assert.AreEqual("HB t=2 fps=60 cpu=-1 rend=-1 gpu=-1 ram=1 scene=a ctx=", noGpu);
        }
    }
}
