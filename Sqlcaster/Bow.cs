using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SqlClient;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Sqlcaster
{
    public class Bow
    {
        private static readonly Regex SqlParameterMatchRegex =
            new Regex("(?<=@)[a-z0-9_]+", RegexOptions.IgnoreCase);

        private static readonly Type[] BasicTypes = new Type[] {
            typeof(bool),
            typeof(bool?),
            typeof(DateTime),
            typeof(DateTime?),
            typeof(DateTimeOffset),
            typeof(DateTimeOffset?),
            typeof(decimal),
            typeof(decimal?),
            typeof(double),
            typeof(double?),
            typeof(Guid),
            typeof(Guid?),
            typeof(int),
            typeof(int?),
            typeof(string),
        };

        public Bow(string connectionStringOrName = null)
        {
            if (connectionStringOrName == null)
            {
                ConnectionString = ConfigurationManager.ConnectionStrings[0].ConnectionString;
            }
            else if (ConfigurationManager.ConnectionStrings[connectionStringOrName] != null)
            {
                ConnectionString = ConfigurationManager.ConnectionStrings[connectionStringOrName].ConnectionString;
            }
            else
            {
                ConnectionString = connectionStringOrName;
            }
        }

        public IQuery<T> Query<T>()
        {
            return new Query<T>(this);
        }

        public string ConnectionString { get; private set; }

        public async Task<SqlConnection> OpenConnectionAsync()
        {
            var c = new SqlConnection(ConnectionString);
            await c.OpenAsync();
            return c;
        }

        public SqlParameter[] GetSqlParameters(string query, object parameters)
        {
            if (query == null)
            {
                throw new ArgumentNullException("query");
            }

            if (parameters == null)
            {
                return new SqlParameter[0];
            }

            var parameterNames = SqlParameterMatchRegex.Matches(query)
                .Cast<Match>().Select(m => m.Value).ToArray();

            var propertyLookup = parameters.GetType().GetProperties()
                .ToDictionary(o => o.Name, StringComparer.InvariantCultureIgnoreCase);

            return parameterNames
                .Select(name => new SqlParameter(name, propertyLookup[name].GetValue(parameters)))
                .ToArray();
        }

        public static Dictionary<string, PropertyInfo> GetPropertyInfo(Type type)
        {
            return type.GetProperties()
                .Where(prop => BasicTypes.Contains(prop.PropertyType) || prop.PropertyType.IsEnum)
                .ToDictionary(o => o.Name, StringComparer.InvariantCultureIgnoreCase);
        }
    }

    public interface IQuery<T>
    {
        IQuery<T> Select(string expression);
        IQuery<T> From(string expression);
        IQuery<T> Where(string expression);
        IQuery<T> OrderBy(string expression);
        IQuery<T> Manifest(Expression<Func<T, object>> property,
            Expression<Func<T, object>> foreignKey = null);
        Task<IList<T>> ToListAsync(object parameters = null, int page = 0, int pageSize = 10);
    }

    public class Query<T> : IQuery<T>
    {
        private static readonly Dictionary<string, PropertyInfo> DefaultProperties;
        private static readonly string Table;

        private readonly Bow Bow;
        private readonly List<string> Selects = new List<string>();
        private readonly List<string> Froms = new List<string>();
        private readonly List<string> Wheres = new List<string>();
        private readonly List<string> OrderBys = new List<string>();
        private readonly List<Manifest<T>> Manifests = new List<Manifest<T>>();

        static Query()
        {
            Table = typeof(T).Name;
            DefaultProperties = Bow.GetPropertyInfo(typeof(T));
        }

        public Query(Bow bow)
        {
            Bow = bow;
        }

        public IQuery<T> Select(string expression)
        {
            Selects.Add(expression);
            return this;
        }

        public IQuery<T> From(string expression)
        {
            Froms.Add(expression);
            return this;
        }

        public IQuery<T> Where(string expression)
        {
            Wheres.Add(expression);
            return this;
        }

        public IQuery<T> OrderBy(string expression)
        {
            OrderBys.Add(expression);
            return this;
        }

        public IQuery<T> Manifest(Expression<Func<T, object>> property,
            Expression<Func<T, object>> foreignKey = null)
        {
            Manifests.Add(new Manifest<T>(property, foreignKey));
            return this;
        }

        public async Task<IList<T>> ToListAsync(object parameters = null, int page = 0, int pageSize = 10)
        {
            string select = Selects.Any() ?
                "select " + string.Join(", ", Selects) :
                "select " + string.Join(", ", DefaultProperties.Keys);
            string where = Wheres.Any() ?
                "\nwhere (" + string.Join(") and (", Wheres) + ")" :
                null;

            var unions = Froms.Any() ?
                string.Join("\nunion all ", Froms.Select(name => select + ", QueryToListAsyncTable = '" + name + "' from " + name + where)) :
                select + ", QueryToListAsyncTable = '" + Table + "' from " + Table + where;

            string orderBy = OrderBys.Any() ?
                "order by " + string.Join(", ", OrderBys) :
                "order by " + string.Join(", ", DefaultProperties.Keys.First());

            string query = string.Join("\n", unions, orderBy, page > 0 ?
                "offset (@QueryToListAsyncPage - 1) rows fetch next @QueryToListAsyncPageSize rows only" :
                null);

            using (var c = await Bow.OpenConnectionAsync())
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = query;
                cmd.Parameters.AddRange(Bow.GetSqlParameters(query, parameters));
                cmd.Parameters.AddRange(new[]
                {
                    new SqlParameter("QueryToListAsyncPage", page),
                    new SqlParameter("QueryToListAsyncPageSize", pageSize),
                });

                var reader = await cmd.ExecuteReaderAsync();
                var fieldNames = Enumerable.Range(0, reader.FieldCount)
                    .Select(i => reader.GetName(i))
                    .Where(name => name != "QueryToListAsyncTable")
                    .ToArray();
                // TODO: Handle when T is an interface.
                var props = typeof(T).GetProperties()
                    .ToDictionary(o => o.Name, StringComparer.InvariantCultureIgnoreCase);

                var list = new List<T>();
                while (await reader.ReadAsync())
                {
                    // TODO: Create instance based on table instead of `T`.
                    string tableName = (string)reader["QueryToListAsyncTable"];
                    T item = Activator.CreateInstance<T>();

                    foreach (var field in fieldNames)
                    {
                        object value = reader[field];
                        // TODO: Make conversion from database field to CLR value configurable.
                        props[field].SetValue(item, value == DBNull.Value ? null : value);
                    }
                    list.Add(item);
                }
                return list;
            }
        }
    }

    internal class Manifest<T>
    {
        private Expression<Func<T, object>> foreignKey;
        private Expression<Func<T, object>> property;

        public Manifest(Expression<Func<T, object>> property,
            Expression<Func<T, object>> foreignKey = null)
        {
            this.property = property;
            this.foreignKey = foreignKey;
        }
    }
}
