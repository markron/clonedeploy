﻿using System;
using System.IO;
using BasePages;
using Helpers;
using Models;
using Pxe;
using Computer = BLL.Computer;

namespace views.hosts
{
    public partial class HostBootMenu : Computers
    {
      

        protected void Page_Load(object sender, EventArgs e)
        {
       


            if (!IsPostBack) DisplayActiveMenu();
        }

        protected void btnRemoveBootMenu_Click(object sender, EventArgs e)
        {
            var customBootMenu = new CustomBootMenu {Host = Computer};
            customBootMenu.RemoveCustomBootMenu();
          
        }

        protected void btnSetBootMenu_Click(object sender, EventArgs e)
        {
            var customBootMenu = new CustomBootMenu {Host = Computer, FileName = txtCustomBootMenu.Text};
            customBootMenu.SetCustomBootMenu();
           
        }

        protected void buttonShowActive_OnClick(object sender, EventArgs e)
        {
            activebootmenu.Visible = true;
            custombootmenu.Visible = false;
            DisplayActiveMenu();
        }

        protected void buttonShowCustom_OnClick(object sender, EventArgs e)
        {
            activebootmenu.Visible = false;
            custombootmenu.Visible = true;
            DisplayCustomMenu();
        }

        protected void ddlTemplate_SelectedIndexChanged(object sender, EventArgs e)
        {
            switch (ddlTemplate.Text)
            {
                case "select template":
                    txtBootMenu.Text = "";
                    break;
                case "default":
                    txtBootMenu.Text = DefaultBootMenu.GetMenuText(ddlProxyMode.Text);
                    break;
                default:
                    var template = new BootTemplate { Name = ddlTemplate.Text };
                    template.Read();
                    txtBootMenu.Text = template.Content;
                    break;
            }
        }

        protected void DisplayActiveMenu()
        {
            var proxyDhcp = Settings.ProxyDhcp;
            var isActive = new Computer().ActiveStatus(Computer.Mac);
            var pxeFileOps = new PxeFileOps();
            string path;

            if (proxyDhcp == "Yes")
                divProxy.Visible = true;

            if (isActive == "Active")
            {
                path = proxyDhcp == "Yes"
                    ? pxeFileOps.GetHostProxyPath(Computer, true, ddlProxyMode.Text)
                    : pxeFileOps.GetHostNonProxyPath(Computer, true);
                lblActiveBoot.Text = "Active Task Found <br> Displaying Task Boot Menu";
            }
            else
            {
                if (Convert.ToBoolean(Convert.ToInt16(Computer.CustomBootEnabled)))
                {
                    path = proxyDhcp == "Yes"
                        ? pxeFileOps.GetHostProxyPath(Computer, true, ddlProxyMode.Text)
                        : pxeFileOps.GetHostNonProxyPath(Computer, true);

                    lblActiveBoot.Text =
                        "No Active Task Found <br> Custom Boot Menu Found <br> Displaying Custom Boot Menu";
                }

                else //Not Active, display default global boot menu
                {
                    path = proxyDhcp == "Yes"
                        ? pxeFileOps.GetHostProxyPath(Computer, false, ddlProxyMode.Text)
                        : pxeFileOps.GetHostNonProxyPath(Computer, false);

                    lblActiveBoot.Text =
                        "No Active Task Found <br> No Custom Boot Menu Found <br> Displaying Global Default Boot Menu";
                }
            }

            lblFileName1.Text = path;
            if (path != null) txtBootMenu.Text = File.ReadAllText(path);
        }

        protected void DisplayCustomMenu()
        {
            ddlTemplate.DataSource = new BootTemplate().ListAll();
            ddlTemplate.DataBind();
            ddlTemplate.Items.Insert(0, "select template");
            ddlTemplate.Items.Insert(1, "default");
        }

        protected void EditProxy_Changed(object sender, EventArgs e)
        {
            DisplayActiveMenu();
        }
    }
}