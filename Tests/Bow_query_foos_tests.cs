using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;
using Sqlcaster;
using Tests.Samples;

namespace Tests
{
    [TestClass]
    public class Bow_query_foos_tests
    {
        private readonly Sqlcaster.Bow bow;

        public Bow_query_foos_tests()
        {
            bow = new Sqlcaster.Bow();
        }

        [TestMethod]
        public async Task _warmup()
        {
            var foos = await bow.Query<Foo>()
                .ToListAsync();
        }

        [TestMethod]
        public async Task List_foos()
        {
            var foos = await bow.Query<Foo>()
                .ToListAsync();
        }

        [TestMethod]
        public async Task Filter_foos()
        {
            var foos = await bow.Query<Foo>()
                .Where("Int between @Start and @Finish")
                .ToListAsync(new { Start = 100, Finish = 200 });
        }

        [TestMethod]
        public async Task Include_foos()
        {
            var foos = await bow.Query<Foo>()
                .Manifest(o => o.Foreign)
                .Manifest(o => o.Children)
                .ToListAsync();
        }

        [TestMethod]
        public async Task Select_foos()
        {
            var foos = await bow.Query<Foo>()
                .Select("Id, Int")
                .ToListAsync();
        }

        [TestMethod]
        public async Task Page_foos()
        {
            var foos = await bow.Query<Foo>()
                .ToListAsync(page: 2, pageSize: 500);
        }
    }
}
