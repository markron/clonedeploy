﻿using System;
using CloneDeploy_Web;
using CloneDeploy_Web.BasePages;
using CloneDeploy_Web.Helpers;


namespace views.computers
{
    public partial class ComputerImport : Computers
    {
        protected void Page_Load(object sender, EventArgs e)
        {
           RequiresAuthorization(Authorizations.CreateComputer);
            if (IsPostBack) return;
          
        }

        protected void ButtonImport_Click(object sender, EventArgs e)
        {
            var csvFilePath = Call.FilesystemApi.GetServerPaths("csv", "computers.csv");
            FileUpload.SaveAs(csvFilePath);
            Call.FilesystemApi.SetUnixPermissions(csvFilePath);
            //var successCount = BLL.Computer.ImportCsv(csvFilePath);
            //EndUserMessage = "Successfully Imported " + successCount + " Computers";

        }

    }
}