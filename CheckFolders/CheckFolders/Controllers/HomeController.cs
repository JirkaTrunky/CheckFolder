using CheckFolders.Models;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using CheckFolders.Lib;
using System.Web;

namespace CheckFolders.Controllers
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
            return RedirectToAction("SelectFolder");
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

        [HttpGet]
        public ActionResult SelectFolder()
        {
            var data = new CheckFolderParams() { FolderName = "~", DeleteTempFiles = false };

            return View(data);
        }


        [HttpPost]
        public ActionResult SelectFolder(CheckFolderParams parametry)
        {
            var data = parametry;

            if (parametry != null)
            { 
                new CheckFolders.Lib.CheckFolders(parametry).DoWork();
            }


            return View(data);
        }
    }
}
