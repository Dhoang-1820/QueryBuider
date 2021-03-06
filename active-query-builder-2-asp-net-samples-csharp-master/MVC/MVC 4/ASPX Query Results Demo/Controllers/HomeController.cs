using System;
using System.Collections;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.OleDb;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using ActiveDatabaseSoftware.ActiveQueryBuilder;
using ActiveDatabaseSoftware.ActiveQueryBuilder.QueryTransformer;
using ActiveDatabaseSoftware.ActiveQueryBuilder.Web.Mvc.Filters;
using ActiveDatabaseSoftware.ActiveQueryBuilder.Web.Server;

namespace MvcAspx.Controllers
{
	public class HomeController : Controller
	{
		public const int PageSize = 10;
		public ActionResult RefreshQueryResultPartial(int page = 1, string sort = "", string sortdir = "")
		{
			ViewBag.CurrentPage = page;

			var queryBuilder = SessionStore.Current.QueryBuilder;
			var queryTransformer = SessionStore.Current.QueryTransformer;

			queryTransformer.Sortings.Clear();
			if (string.IsNullOrEmpty(sortdir)) sortdir = "ASC";
			OutputColumn column = null;
			if (!string.IsNullOrEmpty(sort)) column = queryTransformer.Columns.FirstOrDefault(c => c.Name == sort && c.IsSupportSorting);
			if (column == null) column = queryTransformer.Columns.FirstOrDefault(c => !string.IsNullOrEmpty(c.Name) && c.IsSupportSorting);
			if (column!=null) queryTransformer.OrderBy(column, sortdir == "ASC");

			queryTransformer.Take(PageSize.ToString()).Skip(((page - 1) * PageSize).ToString());

			if (queryBuilder.MetadataProvider != null)
			{
				var cmd = (OleDbCommand)queryBuilder.MetadataProvider.Connection.CreateCommand();
				cmd.CommandTimeout = 30;
				if (SessionStore.Current.QueryTransformer != null)
					cmd.CommandText = SessionStore.Current.QueryTransformer.Sql;
				else
					cmd.CommandText = queryBuilder.SQL;

				try
				{
                    if(cmd.Connection.State == ConnectionState.Open)
                        cmd.Connection.Close();

					foreach (var paramDto in SessionStore.Current.ClientQueryParams)
					{
						cmd.Parameters.AddWithValue(paramDto.Name, paramDto.Value);
					}

					var adapter = new OleDbDataAdapter(cmd);
					var dataset = new DataSet();
					adapter.Fill(dataset, "QueryResult");
					ViewBag.Data = ConvertToDictionary(dataset.Tables["QueryResult"]);
					ViewBag.RowCount = GetRowCount(queryBuilder, queryTransformer);
				    ViewBag.Sql = cmd.CommandText;
				    ViewBag.Sql = cmd.CommandText;
				}
				catch (Exception ex)
				{
					string message = "Execute query error!";
					Logger.Error(message, ex);
					ViewBag.Message = message + " " + ex.Message;
				}
			}

			return PartialView("QueryResult", ViewBag.Data);
		}

        private int GetRowCount(QueryBuilder queryBuilder, QueryTransformer queryTransformer)
		{
			try
			{
				queryBuilder.MetadataProvider.Connection.Open();
			}
			catch (Exception ex)
			{
                string message = "Open connection faild";
                Logger.Error(message, ex);
			    throw;
			}
			queryTransformer.ResultOffset = null;
			queryTransformer.ResultCount = null;
			var selectedColumn = new SelectedColumn(null, "count(*)");
			queryTransformer.Aggregations.Add(selectedColumn, "cnt");
			var cmd = (OleDbCommand)queryBuilder.MetadataProvider.Connection.CreateCommand();
			cmd.CommandTimeout = 30;
			cmd.CommandText = queryTransformer.Sql;
			queryTransformer.Aggregations.Remove(selectedColumn);
			return (int)cmd.ExecuteScalar();
		}

		private List<dynamic> ConvertToDictionary(DataTable dtObject)
		{
			var columns = dtObject.Columns.Cast<DataColumn>();

			var dictionaryList = dtObject.AsEnumerable()
				.Select(dataRow => columns
					.Select(column =>
						new { Column = column.ColumnName, Value = dataRow[column] })
							 .ToDictionary(data => data.Column, data => data.Value)).ToList();

			var list = dictionaryList.ToList<IDictionary>();

			var result = new List<dynamic>();
			foreach (var emprow in list)
			{
				var row = (IDictionary<string, object>)new ExpandoObject();

				foreach (var keyValuePair in (Dictionary<string, object>)emprow)
				{
					row.Add(keyValuePair);
				}
				result.Add(row);
			}
			return result;
		}

        [QueryBuilderInit]
		public ActionResult Index()
		{
			ViewBag.Message = "Modify this template to jump-start your ASP.NET MVC application.";
			
			return View();
		}
    }

    internal class QueryBuilderInitAttribute: InitializeQueryBuilderAttribute
    {
        protected override void Init(ActionExecutingContext filterContext, SessionStoreItem item)
        {
            // Get instance of QueryBuilder
            var queryBuilder = item.QueryBuilder;
            queryBuilder.OfflineMode = false;
            queryBuilder.SyntaxProvider = new MSAccessSyntaxProvider();
            
            try
            {
                // you may load metadata from the database connection using live database connection and metadata provider
                var connection = CreateConnection(filterContext.HttpContext.Server);
                queryBuilder.MetadataProvider = new OLEDBMetadataProvider { Connection = connection };
            }
            catch (Exception ex)
            {
                string message = "Can't setup metadata provider!";
                Logger.Error(message, ex);
                throw;
            }

            try
            {
                queryBuilder.MetadataStructure.Refresh();
            }
            catch (Exception ex)
            {
                string message = "Error loading metadata from the database. Check [web.config] key <configuration>/<connectionStrings><add key=\"YourDB\" connectionString=\"...\">";
                Logger.Error(message, ex);
                SessionStore.Current.Message.Error(message);
                queryBuilder.OfflineMode = true;
            }
        }

        private IDbConnection CreateConnection(HttpServerUtilityBase server)
        {
            //var provider = "Microsoft.ACE.OLEDB.12.0";
            var provider = "Microsoft.Jet.OLEDB.4.0";
            var path = ConfigurationManager.AppSettings["dbpath"];
            var xml = Path.Combine(server.MapPath("~"), path);
            var connectionString = string.Format("Provider={0};Data Source={1};Persist Security Info=False;", provider, xml);

            return new OleDbConnection(connectionString);
        }
    }
}
