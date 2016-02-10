using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;
using Sqlcaster;
using Tests.Samples;
using System.Linq;

namespace Tests
{
    [TestClass]
    public class Bow_query_foos_tests
    {
        private readonly Bow bow;

        public Bow_query_foos_tests()
        {
            bow = new Bow();
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
        public async Task Filter_foos_with_array()
        {
            var foos = await bow.Query<Foo>()
                .Where("Int in @Values")
                .ToListAsync(new { Values = new[] { 100, 200, 300 } });
        }

        [TestMethod]
        public async Task Include_foos()
        {
            var foos = await bow.Query<Foo>()
                //.IncludeReference(o => o.Foreign, o => o.ForeignId)
                //.IncludeChild(o => o.Reference, o => o.FooId)
                //.IncludeChildren(o => o.Children, o => o.Id)
                .ToListAsync();

            await bow.Query<Foreign>()
                .Where("Id in @Ids")
                .FillAsync(foos, foo => foo.Foreign, foo => foo.ForeignId, foreign => foreign.Id);
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
