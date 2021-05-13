using GoogleCivicDataParse.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace GoogleCivicDataParse.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        public JsonResult ParseData(string apiKey, string address)
        {

            string apiURL = $"https://www.googleapis.com/civicinfo/v2/representatives?address={HttpUtility.UrlEncode(address)}&key={apiKey}&levels=country&levels=administrativeArea1&roles=legislatorUpperBody&roles=legislatorLowerBody";

            var client = new RestClient();
            var request = new RestRequest(apiURL, Method.GET);
            IRestResponse response = client.Execute<JObject>(request);

            var resObj = JObject.Parse(response.Content);
            if (response.StatusCode == HttpStatusCode.OK && resObj.ContainsKey("divisions"))
            {
                var finalList = new List<GoogleCivicDivison>();
                var divisions = resObj.Value<JObject>("divisions").Properties();
                var officials = JsonConvert.DeserializeAnonymousType(resObj.Value<JArray>("officials").ToString(), new[] { new { name = string.Empty } });
                var offices = JsonConvert.DeserializeObject<List<GoogleCivicOffice>>(resObj.Value<JArray>("offices").ToString());

                foreach (var office in offices)
                {
                    if (office.OfficialIndices == null || office.OfficialIndices.Count == 0) continue;

                    office.Officials = new List<string>();
                    foreach (var i in office.OfficialIndices) office.Officials.Add(officials[i].name);


                    var divison = finalList.FirstOrDefault(x => x.DivisionId == office.DivisionId);
                    if (divison != null)
                    {
                        divison.Offices.Add(office);
                        continue;
                    }

                    divison = new GoogleCivicDivison();
                    var d = divisions.FirstOrDefault(x => x.Name == office.DivisionId);

                    divison.DivisionId = d.Name;
                    divison.DivisionName = d.Value.Value<JObject>().GetValue("name").Value<string>();
                    var indices = d.Value.Value<JObject>().GetValue("officeIndices").Select(x => x.Value<int>());
                    if (indices != null && indices.Any())
                        divison.Displayorder = indices.FirstOrDefault();

                    divison.Offices.Add(office);
                    finalList.Add(divison);

                }
                finalList = finalList.OrderBy(c => c.Displayorder).ToList();

                return Json(new
                {
                    success = true,
                    data = new
                    {
                        data = finalList,
                        displayText = ParepareDisplayText(finalList),

                    }
                });
            }
            else
            {
                var error = JsonConvert.DeserializeAnonymousType(response.Content, new { error = new { code = 0, message = "" } });
                return Json(new { success = false, errorMessage = $"{error?.error?.code}: {error?.error?.message}" });
            }


        }

        public string ParepareDisplayText(List<GoogleCivicDivison> divisons)
        {
            StringBuilder builder = new StringBuilder();
            foreach (var d in divisons)
            {
                builder.AppendLine(d.DivisionName);
                foreach (var o in d.Offices)
                    foreach (var name in o.Officials)
                        builder.AppendLine($"{o.Name}: {name}");

                builder.AppendLine("");
            }


            builder.AppendLine("");
            builder.AppendLine("");
            builder.AppendLine("");
            builder.AppendLine("");

            var offices = new List<GoogleCivicOffice>();
            foreach (var d in divisons)
                offices.AddRange(d.Offices ?? new List<GoogleCivicOffice>());

            builder.AppendLine($"<b>National Senate:</b> {GetOfficialsName(offices, "country", "legislatorUpperBody")}");

            string districtNumber = GetDistrictNumber(offices, "country", "legislatorLowerBody");
            if(!string.IsNullOrWhiteSpace(districtNumber))
                builder.AppendLine($"<b>Congressional  District Number:</b> {districtNumber}");

            builder.AppendLine($"<b>Congressional District:</b> {GetDistrictName(divisons, offices, "country", "legislatorLowerBody")}");
            builder.AppendLine($"<b>Congressional Representative:</b> {GetOfficialsName(offices, "country", "legislatorLowerBody")}");

            districtNumber = GetDistrictNumber(offices, "administrativeArea1", "legislatorUpperBody");
            if (!string.IsNullOrWhiteSpace(districtNumber))
                builder.AppendLine($"<b>State Upper House District Number:</b> {districtNumber}");
            builder.AppendLine($"<b>State Upper House District:</b>  {GetDistrictName(divisons, offices, "administrativeArea1", "legislatorUpperBody")}");
            builder.AppendLine($"<b>State Upper House Representative:</b>  {GetOfficialsName(offices, "administrativeArea1", "legislatorUpperBody")}");

            districtNumber = GetDistrictNumber(offices, "administrativeArea1", "legislatorLowerBody");
            if (!string.IsNullOrWhiteSpace(districtNumber))
                builder.AppendLine($"<b>State Lower House District Number:</b> {districtNumber}");
            builder.AppendLine($"<b>State Lower House District:</b>  {GetDistrictName(divisons, offices, "administrativeArea1", "legislatorLowerBody")}");
            builder.AppendLine($"<b>State Lower House Representative:</b>  {GetOfficialsName(offices, "administrativeArea1", "legislatorLowerBody")}");



            return builder.ToString().Replace("\n", "<br/>");
        }

        public string GetOfficialsName(List<GoogleCivicOffice> offices, string level, string role)
        {
            if (offices == null) return "";
            return string.Join(", ", (offices.FirstOrDefault(x => x.Roles.Contains(role) && x.Levels.Contains(level))?.Officials ?? new List<string>()).ToArray());
        }
        public string GetDistrictNumber(List<GoogleCivicOffice> offices, string level, string role)
        {
            if (offices == null) return "";
            string divisonId = offices.FirstOrDefault(x => x.Roles.Contains(role) && x.Levels.Contains(level))?.DivisionId;

            if (string.IsNullOrWhiteSpace(divisonId) || !divisonId.Contains(":")) return ""; 
            return divisonId.Split(':').LastOrDefault();
        }
        public string GetDistrictName(List<GoogleCivicDivison> divisons, List<GoogleCivicOffice> offices, string level, string role)
        {
            if (offices == null) return "";


            string divisonId = offices.FirstOrDefault(x => x.Roles.Contains(role) && x.Levels.Contains(level))?.DivisionId;
            return divisons.FirstOrDefault(x => x.DivisionId == divisonId)?.DivisionName;
        }
    }
}


public class GoogleCivicOffice
{
    public GoogleCivicOffice()
    {
        Roles = new List<string>();
        Levels = new List<string>();
    }
    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("divisionId")]
    public string DivisionId { get; set; }

    [JsonProperty("levels")]
    public List<string> Levels { get; set; }

    [JsonProperty("roles")]
    public List<string> Roles { get; set; }

    [JsonProperty("officialIndices")]
    public List<int> OfficialIndices { get; set; }

    [JsonProperty("officials")]
    public List<string> Officials { get; set; }
}

public class GoogleCivicDivison
{
    public GoogleCivicDivison()
    {
        Offices = new List<GoogleCivicOffice>();
    }

    [JsonProperty("divisionName")]
    public string DivisionName { get; set; }

    [JsonProperty("divisionId")]
    public string DivisionId { get; set; }

    [JsonProperty("offices")]
    public List<GoogleCivicOffice> Offices { get; set; }

    [JsonProperty("displayOrder")]
    public int Displayorder { get; set; }
}

