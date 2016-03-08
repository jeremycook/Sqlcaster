using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SqlClient;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
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

        public SqlParameter[] GetSqlParameters(ref string query, object parameters)
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

            var sqlparameters = new List<SqlParameter>(parameterNames.Length);
            foreach (var name in parameterNames)
            {
                var prop = propertyLookup[name];
                object value = prop.GetValue(parameters);

                if (prop.PropertyType.IsArray)
                {
                    var array = ((Array)value).Cast<object>().ToArray();
                    var newParameters = Enumerable.Range(0, array.Length)
                        .Select(num => name + num)
                        .ToArray();

                    query = Regex.Replace(query, @"@" + Regex.Escape(name) + @"\b",
                        "(@" + string.Join(", @", newParameters) + ")");
                    sqlparameters.AddRange(array.Select((o, num) => new SqlParameter(name + num, o)));
                }
                else
                {
                    sqlparameters.Add(new SqlParameter(name, value));
                }
            }
            return sqlparameters.ToArray();
        }

        public static Dictionary<string, PropertyInfo> GetPropertyInfo(Type type)
        {
            return type.GetProperties()
                .Where(prop => BasicTypes.Contains(prop.PropertyType) || prop.PropertyType.IsEnum)
                .ToDictionary(o => o.Name, StringComparer.InvariantCultureIgnoreCase);
        }

        public static TableInfo GetTableInfo(Type type)
        {
            return new TableInfo
            {
                Name = type.Name,
                SqlName = "[" + type.Name + "]",
            };
        }
    }

    public class TableInfo
    {
        public string Name { get; set; }
        public string SqlName { get; set; }
    }

    public interface IQuery<T>
    {
        IQuery<T> Select(string expression);
        IQuery<T> From(string expression);
        IQuery<T> Where(string expression);
        IQuery<T> OrderBy(string expression);
        Task<IList<T>> ToListAsync(object parameters = null, int page = 0, int pageSize = 10);
    }

    public class Query<T> : IQuery<T>
    {
        private static readonly Dictionary<string, PropertyInfo> DefaultProperties;
        private static readonly TableInfo TableInfo;

        private readonly Bow Bow;
        private readonly List<string> Selects = new List<string>();
        private readonly List<string> Froms = new List<string>();
        private readonly List<string> Wheres = new List<string>();
        private readonly List<string> OrderBys = new List<string>();

        static Query()
        {
            TableInfo = Bow.GetTableInfo(typeof(T));
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
                select + ", QueryToListAsyncTable = '" + TableInfo.Name + "' from " + TableInfo.SqlName + where;

            string orderBy = OrderBys.Any() ?
                "order by " + string.Join(", ", OrderBys) :
                "order by " + string.Join(", ", DefaultProperties.Keys.First());

            string query = string.Join("\n", unions, orderBy, page > 0 ?
                "offset (@QueryToListAsyncPage - 1) rows fetch next @QueryToListAsyncPageSize rows only" :
                null);

            using (var c = await Bow.OpenConnectionAsync())
            using (var cmd = c.CreateCommand())
            {
                cmd.Parameters.AddRange(Bow.GetSqlParameters(ref query, parameters));
                cmd.Parameters.AddRange(new[]
                {
                    new SqlParameter("QueryToListAsyncPage", page),
                    new SqlParameter("QueryToListAsyncPageSize", pageSize),
                });
                cmd.CommandText = query;

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

    public static class QueryExtensions
    {
        public static async Task FillAsync<T, TTarget, TKey>(
            this IQuery<T> query,
            IEnumerable<TTarget> targets,
            Expression<Func<TTarget, T>> reference,
            Func<TTarget, TKey> key,
            Func<T, TKey> primaryKey)
        {
            var actualTargets = targets.Select(t => new { t, key = key(t) })
                .Where(o => o.key != null)
                .ToDictionary(o => o.key, o => o.t);

            var references = await query.ToListAsync(new { Ids = actualTargets.Keys.ToArray() });

            var prop = Helpers.GetPropertyInfo(reference);
            foreach (var r in references)
            {
                var pk = primaryKey(r);
                var target = actualTargets[pk];
                prop.SetValue(target, r);
            }
        }

    }

    public static class EnumerableExtensions
    {
        public static void FillWith<T, TReference, TKey>(
            this IEnumerable<T> list,
            IEnumerable<TReference> references,
            Expression<Func<T, TReference>> referenceSelector,
            Func<T, TKey> listKeySelector,
            Func<TReference, TKey> referenceKeySelector)
        {
            var prop = Helpers.GetPropertyInfo(referenceSelector);

            var dict = list
                .GroupBy(o => listKeySelector(o))
                .Where(o => o.Key != null)
                .ToDictionary(o => o.Key, o => o.AsEnumerable());

            foreach (var reference in references.ToDictionary(o => referenceKeySelector(o)))
            {
                foreach (var item in dict[reference.Key])
                {
                    prop.SetValue(item, reference.Value);
                }
            }
        }

        public static void FillWith<T, TReference, TKey>(
            this IEnumerable<T> list,
            IEnumerable<TReference> references,
            Expression<Func<T, IEnumerable<TReference>>> referenceSelector,
            Func<T, TKey> listKeySelector,
            Func<TReference, TKey> referenceKeySelector)
        {
            var prop = Helpers.GetPropertyInfo(referenceSelector);

            var dict = list.GroupBy(o => listKeySelector(o))
                .ToDictionary(o => o.Key, o => o.AsEnumerable());

            foreach (var group in references.GroupBy(o => referenceKeySelector(o)))
            {
                foreach (var item in dict[group.Key])
                {
                    IEnumerable<TReference> children;
                    if (prop.PropertyType.IsArray)
                    {
                        children = group.ToArray();
                    }
                    else
                    {
                        children = group.ToList();
                    }

                    prop.SetValue(item, children);
                }
            }
        }
    }

    public static class Helpers
    {
        public static PropertyInfo GetPropertyInfo(LambdaExpression exp)
        {
            MemberExpression body = exp.Body as MemberExpression;

            if (body == null)
            {
                UnaryExpression ubody = (UnaryExpression)exp.Body;
                body = ubody.Operand as MemberExpression;
            }

            return body.Member as PropertyInfo;
        }
    }
}
