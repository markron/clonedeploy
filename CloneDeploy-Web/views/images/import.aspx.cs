﻿using System;
using CloneDeploy_Web;
using CloneDeploy_Web.BasePages;
using CloneDeploy_Web.Helpers;

namespace views.images
{
    public partial class ImageImport : Images
    {
        protected void Page_Load(object sender, EventArgs e)
        {
            RequiresAuthorization(Authorizations.CreateImage);
            if (IsPostBack) return;

        }

        protected void ButtonImport_Click(object sender, EventArgs e)
        {
            var csvFilePath = Call.FilesystemApi.GetServerPaths("csv", "images.csv");
            FileUpload.SaveAs(csvFilePath);
            Call.FilesystemApi.SetUnixPermissions(csvFilePath);
            //var successCount = BLL.Image.ImportCsv(csvFilePath);
            //EndUserMessage = "Successfully Imported " + successCount + " Images";

        }       
    }
}