using System.Collections.Generic;
using Newtonsoft.Json;

namespace StackAnalyzer.Common
{
	public class Wrapper
	{
		public List<dynamic> Items { get; set; }

		public bool HasMore { get; set; }

		public int QuotaRemaining { get; set; }

		public int Page { get; set; }

		public int PageSize { get; set; }

		public int Total { get; set; }

		public int? ErrorId { get; set; }

		public int? Backoff { get; set; }

		public string ErrorMessage { get; set; }

		public string ErrorName { get; set; }
	}
}
