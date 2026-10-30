using System.Text;
using NUnit.Framework;

namespace QA.PerfKit.Tests
{
    public sealed class PerfContextTests
    {
        private static string Render(PerfContext context)
        {
            var sb = new StringBuilder();
            context.AppendTo(sb);
            return sb.ToString();
        }

        [Test]
        public void Set_strips_separators_and_caps_value()
        {
            var context = new PerfContext();

            context.Set("a", "x;y=z w");
            Assert.AreEqual("a:xyzw", Render(context));

            context.Set("a", new string('v', 40));
            Assert.AreEqual("a:" + new string('v', PerfContext.MAX_VALUE), Render(context));
            Assert.AreEqual(1, context.Count);
        }

        [Test]
        public void Ninth_key_is_ignored()
        {
            var context = new PerfContext();
            for (int i = 0; i < PerfContext.MAX_KEYS; i++) context.Set("k" + i, "v");
            Assert.AreEqual(8, context.Count);

            context.Set("k8", "v");

            Assert.AreEqual(8, context.Count);
            StringAssert.DoesNotContain("k8", Render(context));

            // An existing key is still updated when the table is full.
            context.Set("k0", "new");
            StringAssert.StartsWith("k0:new;", Render(context));
        }

        [Test]
        public void Clear_removes_key_and_AppendTo_keeps_order()
        {
            var context = new PerfContext();
            context.Set("a", "1");
            context.Set("b", "2");
            context.Set("c", "3");
            Assert.AreEqual("a:1;b:2;c:3", Render(context));

            context.Clear("b");
            Assert.AreEqual(2, context.Count);
            Assert.AreEqual("a:1;c:3", Render(context));

            context.Clear("missing");
            Assert.AreEqual("a:1;c:3", Render(context));

            context.Set("b", "2");
            Assert.AreEqual("a:1;c:3;b:2", Render(context));
        }
    }
}
