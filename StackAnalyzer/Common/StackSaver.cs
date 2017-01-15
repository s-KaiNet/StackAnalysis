using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using NLog;
using RestSharp;
using RestSharp.Newtonsoft.Json;
using RestRequest = RestSharp.RestRequest;

namespace StackAnalyzer.Common
{
	public class StackSaver
	{
		private static string ApiUrl = ConfigurationManager.AppSettings["ApiUrl"];
		private static string Key = ConfigurationManager.AppSettings["Key"];
		private static string AccessToken = ConfigurationManager.AppSettings["AccessToken"];

		private const int PageSize = 100;

		private readonly string _resource;
		private readonly int _count;
		private readonly IRestClient _client;
		private readonly ILogger _logger;
		private List<Parameter> _additionalParameters;
		private readonly string _site;

		public StackSaver(string resource, string site, List<Parameter> additionalParameters = null, int count = Int32.MaxValue)
		{
			_resource = resource;
			_count = count;
			if (additionalParameters == null)
			{
				_additionalParameters = new List<Parameter>();
			}
			else
			{
				_additionalParameters = additionalParameters;
			}

			_client = new RestClient(ApiUrl);
			_logger = LogManager.GetCurrentClassLogger();
			_site = site;
		}

		public void LoadAndSaveData(string fileName, int startPage = 1, bool merge = false)
		{
			var restRequest = new RestRequest($"2.2/{_resource}", Method.GET);
			restRequest.JsonSerializer = new NewtonsoftJsonSerializer();

			foreach (var parameter in _additionalParameters)
			{
				restRequest.AddParameter(parameter.Name, parameter.Value);
			}

			restRequest.AddParameter("site", _site);
			restRequest.AddParameter("key", Key);
			restRequest.AddParameter("access_token", AccessToken);
			restRequest.AddParameter("pagesize", 100);
			restRequest.AddParameter("page", startPage);

			var results = new List<dynamic>();

			try
			{
				LoadResults(restRequest, startPage, results);
				SaveFile(fileName, merge, results);
			}
			catch (Exception)
			{
				SaveFile(fileName, merge, results);
				throw;
			}
		}

		private void LoadResults(IRestRequest request, int page, List<dynamic> items, int iteration = 1)
		{
			if (items.Count >= _count)
			{
				return;
			}

			var pageSize = PageSize;

			if (pageSize * iteration > _count)
			{
				pageSize = _count - pageSize*(iteration - 1);
			}

			var pageParam = request.Parameters.Single(p => p.Name == "page");
			var pageSizeParam = request.Parameters.Single(p => p.Name == "pagesize");

			pageParam.Value = page;
			pageSizeParam.Value = pageSize;

			_logger.Info($"Runing request: {_client.BuildUri(request)}");
			_logger.Info($"Page: {page}");

			Thread.Sleep(200); //prevent throttling from stack exchange when too many requests per second

			var results = _client.Execute<Wrapper>(request);

			_logger.Info($"Total items: {results.Data.Total}");

			if (results.Data.Backoff != null)
			{
				//when backoff received, sleep for specific acmount of seconds provided in backoff
				_logger.Info($"Backoff received, sleeping {results.Data.Backoff} seconds");
				Thread.Sleep(results.Data.Backoff.Value * 1000);
			}

			if (!string.IsNullOrEmpty(results.Data.ErrorMessage))
			{
				_logger.Error($"{results.Data.ErrorName}");
				_logger.Error($"{results.Data.ErrorMessage}");

				throw new Exception(results.Data.ErrorMessage);
			}

			items.AddRange(results.Data.Items);

			if (!results.Data.HasMore)
			{
				return;
			}

			_logger.Info($"Quota remaining: {results.Data.QuotaRemaining}");
			_logger.Info("");

			LoadResults(request, page + 1, items, iteration + 1);
		}

		private void SaveFile(string filename, bool merge, List<dynamic> items)
		{
			if (merge && File.Exists(filename))
			{
				var existingItems = JsonConvert.DeserializeObject<List<dynamic>>(File.ReadAllText(filename));
				_logger.Info($"Merging results with existing file, existing items count: {existingItems.Count}");
				items.AddRange(existingItems);
			}

			if (merge && !File.Exists(filename))
			{
				_logger.Info($"Unable to find file with name '${filename}' for merging");
			}

			File.WriteAllText(filename, JsonConvert.SerializeObject(items));

			_logger.Info("File written");
		}
	}
}
