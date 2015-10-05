﻿using System.Web;
using BLL;
using Models;

namespace Security
{
    /// <summary>
    ///     Summary description for Authorize
    /// </summary>
    public class Authorize
    {
        public bool IsInMembership(string membership)
        {
            WdsUser user = new User().GetUser(HttpContext.Current.User.Identity.Name);
            return membership == user.Membership;
        }
    }
}