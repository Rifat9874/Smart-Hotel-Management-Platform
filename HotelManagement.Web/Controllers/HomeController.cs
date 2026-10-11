using HotelManagement.Web.Data;
using HotelManagement.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace HotelManagement.Web.Controllers;

public class HomeController : AppController
{
    public HomeController(Db db, Clock clock) : base(db, clock) { }

    public IActionResult Index() => View();
}
