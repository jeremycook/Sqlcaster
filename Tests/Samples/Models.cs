using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tests.Samples
{
    public interface IId
    {
        Guid Id { get; }
        string Name { get; }
    }

    public class Foo : IId
    {
        public Guid Id { get; private set; }
        public string Name { get; private set; }

        public int Int { get; private set; }

        public Guid ForeignId { get; private set; }
        public Foo Foreign { get; private set; }

        public FooReference Reference { get; private set; }

        public FooChild[] Children { get; private set; }
    }

    public class FooReference
    {
        public Guid FooId { get; private set; }
        public string Name { get; private set; }
    }

    public class FooChild : IId
    {
        public Guid Id { get; private set; }
        public string Name { get; private set; }
    }
}
