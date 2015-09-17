﻿<%@ Page Title="" Language="C#" MasterPageFile="~/views/computers/computers.master" AutoEventWireup="true" Inherits="views.hosts.HostImport" CodeFile="import.aspx.cs" %>

<%@ MasterType VirtualPath="~/views/computers/computers.master" %>
<%@ Reference virtualPath="~/views/masters/Site.master" %>
<asp:Content ID="Content" ContentPlaceHolderID="SubContent" runat="Server">
    <script type="text/javascript">
        $(document).ready(function() {
            $('#importhost').addClass("nav-current");
        });
    </script>
    <div class="size-4 column">
        <asp:FileUpload ID="FileUpload" runat="server"/>
        <asp:LinkButton ID="buttonImport" runat="server" Text="Upload" OnClick="ButtonImport_Click" CssClass="submits"/>
    </div>
    <br class="clear"/>
</asp:Content>