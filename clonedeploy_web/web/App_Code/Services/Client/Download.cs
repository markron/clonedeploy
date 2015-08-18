﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Global;
using Models;
using Newtonsoft.Json;
using Partition;
using RegFileParser;

namespace Services.Client
{
    public class Download
    {
        //Only used for mbr partition scheme.  GPT identifies offsets to BCD through the guid of the partition which is restored
        //when the image is restored
        public string AlignBcdToPartition(string bcd, string newOffsetBytes)
        {
            var newOffsetHex = Convert.ToInt64(newOffsetBytes).ToString("X16").Reverse().ToList();
            StringBuilder output = new StringBuilder();

            for (int i = 0; i < newOffsetHex.Count; i++)
            {
                if (i % 2 == 0)
                {
                    if ((i + 1) < newOffsetHex.Count)
                    {
                        output.Append(newOffsetHex[i + 1]);
                    }
                    output.Append(newOffsetHex[i]);

                    if (i + 2 != newOffsetHex.Count)
                        output.Append(",");
                }
            }
            string newOffsetHexReversed = output.ToString();

            List<string> guids = new List<string>();
            RegFileObject regfile = new RegFileObject(bcd);
            foreach (var reg in regfile.RegValues)
            {
                var tmp = reg;
                foreach (var abc in tmp.Value.Values)
                {
                    if (abc.Value.ToLower().Contains("winload.exe"))
                    {
                        var matches = Regex.Matches(reg.Key, @"\{(.*?)\}");
                        guids.AddRange(from Match m in matches select m.Groups[1].Value);
                    }
                }
            }

            guids = guids.Distinct().ToList();
            foreach (var guid in guids)
            {
                var regBinary =
                    regfile.RegValues[@".\Objects\{"+guid+@"}\Elements\11000001"]["Element"]
                        .Value;
                var regBinarySplit = regBinary.Split(',');
                var originalOffsetHex = regBinarySplit[32] + "," + regBinarySplit[33] + "," + regBinarySplit[34] + "," + regBinarySplit[35] + "," +
                               regBinarySplit[36] + "," + regBinarySplit[37] + "," + regBinarySplit[38] + "," + regBinarySplit[39];

                var regex = new Regex(originalOffsetHex, RegexOptions.IgnoreCase);
                bcd = regex.Replace(bcd, newOffsetHexReversed);

            }

            return Utility.Encode(bcd);
        }

        public string AmINext()
        {
            using (var db = new DB())
            {
                return (from a in db.ActiveTasks where a.Type == "unicast" && a.Status == "2" orderby a.QueuePosition select a.QueuePosition).FirstOrDefault().ToString();
            }
        }

        public string CurrentQueuePosition(string mac)
        {
            using (var db = new DB())
            {
                var currentPosition =(from h in db.Hosts 
                           join t in db.ActiveTasks on h.Name equals t.Name
                           where (h.Mac.ToLower() == mac.ToLower())
                           select t.QueuePosition).FirstOrDefault();
                int intCurrentPosition = Convert.ToInt16(currentPosition);
                return db.ActiveTasks.Count(t => t.QueuePosition < intCurrentPosition).ToString();
            }
        }

        public string GetFileNames(string imageName)
        {
            var dirs = new List<string>();
            string result = null;
            var path = Settings.ImageStorePath + imageName;
            dirs.Add(path);

            for (var x = 2;; x++)
            {
                var subdir = path + Path.DirectorySeparatorChar + "hd" + x;
                if (Directory.Exists(subdir))
                    dirs.Add(subdir);
                else
                    break;
            }

            foreach (var dirPath in dirs)
            {
                var files = Directory.GetFiles(dirPath, "*.*");
                foreach (var file in files)
                    result += Path.GetFileName(file) + ";";

                result += ",";
            }

            return result;
        }

        public string GetHdParameter(string imgName, string hdToGet, string partNumber, string paramName)
        {
            var image = new Image {Name = imgName};
            image.Read();
            var specs =
                JsonConvert.DeserializeObject<ImagePhysicalSpecs>(!string.IsNullOrEmpty(image.ClientSizeCustom)
                    ? image.ClientSizeCustom
                    : image.ClientSize);

            var activeCounter = Convert.ToInt32(hdToGet);
            var hdNumberToGet = Convert.ToInt32(hdToGet) - 1;

            //Look for first active hd
            if (specs.Hd[hdNumberToGet].Active != "1")
            {
                while (activeCounter <= specs.Hd.Count())
                {
                    if (specs.Hd[activeCounter - 1].Active == "1")
                    {
                        hdNumberToGet = activeCounter - 1;
                    }
                    activeCounter++;
                }
            }

            switch (paramName)
            {
                case "uuid":
                    foreach (var partition in specs.Hd[hdNumberToGet].Partition)
                        if (partition.Number == partNumber)
                        {
                            return partition.Uuid;
                        }
                    break;
                case "lvmswap":
                    foreach (var partition in specs.Hd[hdNumberToGet].Partition)
                    {
                        if (partition.Vg == null) continue;
                        if (partition.Vg.Lv == null) continue;
                        foreach (var lv in partition.Vg.Lv)
                        {
                            if (lv.FsType.ToLower() != "swap" || lv.Active != "1") continue;
                            return lv.Vg.Replace("-", "--") + "-" +
                                   lv.Name.Replace("-", "--") + "," + lv.Uuid;
                        }
                    }
                    break;
                case "boot":
                    return specs.Hd[hdNumberToGet].Boot;
                case "guid":
                    foreach (var partition in specs.Hd[hdNumberToGet].Partition)
                        if (partition.Number == partNumber)
                        {
                            return partition.Guid;
                        }
                    break;
                case "table":
                    return specs.Hd[hdNumberToGet].Table;

                case "partCount":
                    var activePartsCounter = specs.Hd[hdNumberToGet].Partition.Count(partition => partition.Active == "1");
                    return activePartsCounter.ToString();

                case "activeParts":
                {
                    string parts = null;
                    foreach (var part in specs.Hd[hdNumberToGet].Partition.Where(part => part.Active == "1"))
                    {
                        string imageFiles;
                        if (hdNumberToGet == 0)
                            imageFiles = Settings.ImageStorePath + imgName;
                        else
                            imageFiles = Settings.ImageStorePath + imgName + Path.DirectorySeparatorChar +
                                         "hd" + (hdNumberToGet + 1);

                        try
                        {
                            bool filesFound = false;
                            foreach (var ext in new string[2] {"*.gz", "*.lz4"})
                            {
                                var partFiles = Directory.GetFiles(imageFiles + Path.DirectorySeparatorChar, ext);
                                if (partFiles.Length > 0)
                                    filesFound = true;
                            }
                            if(!filesFound)
                                Logger.Log("Image Files Could Not Be Located");
                          
                        }
                        catch
                        {
                            Logger.Log("Image Files Could Not Be Located");
                        }

                        if (
                            File.Exists(imageFiles + Path.DirectorySeparatorChar + "part" + part.Number + ".ntfs" +
                                        ".gz") || File.Exists(imageFiles + Path.DirectorySeparatorChar + "part" + part.Number + ".ntfs" +
                                        ".lz4"))
                            parts += part.Number + " ";
                        else if (
                            File.Exists(imageFiles + Path.DirectorySeparatorChar + "part" + part.Number + ".fat" +
                                        ".gz") || File.Exists(imageFiles + Path.DirectorySeparatorChar + "part" + part.Number + ".fat" +
                                        ".lz4"))
                            parts += part.Number + " ";
                        else if (
                            File.Exists(imageFiles + Path.DirectorySeparatorChar + "part" + part.Number +
                                        ".extfs" + ".gz") || File.Exists(imageFiles + Path.DirectorySeparatorChar + "part" + part.Number +
                                        ".extfs" + ".lz4"))
                            parts += part.Number + " ";
                        else if (
                            File.Exists(imageFiles + Path.DirectorySeparatorChar + "part" + part.Number +
                                        ".hfsp" + ".gz") || File.Exists(imageFiles + Path.DirectorySeparatorChar + "part" + part.Number +
                                        ".hfsp" + ".lz4"))
                            parts += part.Number + " ";
                        else if (
                            File.Exists(imageFiles + Path.DirectorySeparatorChar + "part" + part.Number +
                                        ".imager" + ".gz") || File.Exists(imageFiles + Path.DirectorySeparatorChar + "part" + part.Number +
                                        ".imager" + ".lz4"))
                            parts += part.Number + " ";
                    }
                    return parts;
                }

                case "lvmactiveParts":
                {
                    string parts = null;
                    string compExt = null;
                    foreach (var part in specs.Hd[hdNumberToGet].Partition.Where(part => part.Active == "1"))
                    {
                        string imageFiles;
                        if (hdNumberToGet == 0)
                            imageFiles = Settings.ImageStorePath + imgName;
                        else
                            imageFiles = Settings.ImageStorePath + imgName + Path.DirectorySeparatorChar +
                                         "hd" + (hdNumberToGet + 1);

                        try
                        {
                            var partFiles = Directory.GetFiles(imageFiles + Path.DirectorySeparatorChar, "*.gz*");
                            if (partFiles.Length == 0)
                            {
                                partFiles = Directory.GetFiles(imageFiles + Path.DirectorySeparatorChar, "*.lz4*");
                                if (partFiles.Length == 0)
                                {
                                    Logger.Log("Image Files Could Not Be Located");
                                }
                                else
                                {
                                    compExt = ".lz4";
                                }
                            }
                            else
                            {
                                compExt = ".gz";
                            }
                        }
                        catch
                        {
                            Logger.Log("Image Files Could Not Be Located");
                        }
                        if (part.Vg == null) continue;
                        if (part.Vg.Lv == null) continue;
                        foreach (var lv in part.Vg.Lv.Where(lv => lv.Active == "1"))
                        {
                            if (
                                File.Exists(imageFiles + Path.DirectorySeparatorChar + lv.Vg + "-" + lv.Name +
                                            ".ntfs" + compExt))
                                parts += lv.Vg + "-" + lv.Name + " ";
                            else if (
                                File.Exists(imageFiles + Path.DirectorySeparatorChar + lv.Vg + "-" + lv.Name +
                                            ".fat" + compExt))
                                parts += lv.Vg + "-" + lv.Name + " ";
                            else if (
                                File.Exists(imageFiles + Path.DirectorySeparatorChar + lv.Vg + "-" +
                                            lv.Name + ".extfs" + compExt))
                                parts += lv.Vg.Replace("-", "--") + "-" + lv.Name.Replace("-", "--") +
                                         " ";
                            else if (
                                File.Exists(imageFiles + Path.DirectorySeparatorChar + lv.Vg + "-" +
                                            lv.Name + ".hfsp" + compExt))
                                parts += lv.Vg + "-" + lv.Name + " ";
                            else if (
                                File.Exists(imageFiles + Path.DirectorySeparatorChar + lv.Vg +
                                            "-" + lv.Name + ".imager" + compExt))
                                parts += lv.Vg + "-" + lv.Name + " ";
                        }
                    }
                    return parts;
                }
                case "HDguid":
                    return specs.Hd[hdNumberToGet].Guid;

                case "originalHDSize":
                    var originalSize =
                        (Convert.ToInt64(specs.Hd[hdNumberToGet].Size)*Convert.ToInt32(specs.Hd[hdNumberToGet].Lbs))
                            .ToString();
                    return originalSize;

                case "isKnownLayout":
                    switch (specs.Hd[hdNumberToGet].Table)
                    {
                        case "mbr":
                            switch (specs.Hd[hdNumberToGet].Partition.Count())
                            {
                                case 1:
                                    switch (specs.Hd[hdNumberToGet].Partition[0].Start)
                                    {
                                        case "63":
                                            return "winxp";

                                        case "2048":
                                            return "winvista";

                                        default:
                                            return "false";
                                    }

                                case 2:
                                    var part1Size =
                                        ((Convert.ToInt64(specs.Hd[hdNumberToGet].Partition[0].Size)*
                                          Convert.ToInt32(specs.Hd[hdNumberToGet].Lbs)/1024/1024)).ToString();
                                    if (specs.Hd[hdNumberToGet].Partition[0].Start == "2048" && part1Size == "100")
                                        return "win7";
                                    if (specs.Hd[hdNumberToGet].Partition[0].Start == "2048" && part1Size == "350")
                                        return "win8";
                                    return "false";

                                default:
                                    return "false";
                            }

                        case "gpt":
                            switch (specs.Hd[hdNumberToGet].Partition.Count())
                            {
                                case 3:
                                    if (specs.Hd[hdNumberToGet].Partition[0].Start == "2048" &&
                                        specs.Hd[hdNumberToGet].Partition[1].Start == "206848" &&
                                        specs.Hd[hdNumberToGet].Partition[2].Start == "468992")
                                        return "win7gpt";
                                    return "false";

                                case 4:
                                    if (specs.Hd[hdNumberToGet].Partition[0].Start == "2048" &&
                                        specs.Hd[hdNumberToGet].Partition[1].Start == "616448" &&
                                        specs.Hd[hdNumberToGet].Partition[2].Start == "821248" &&
                                        specs.Hd[hdNumberToGet].Partition[3].Start == "1083392")
                                        return "win8gpt";
                                    if (specs.Hd[hdNumberToGet].Partition[0].Start == "2048" &&
                                        specs.Hd[hdNumberToGet].Partition[1].Start == "616448" &&
                                        specs.Hd[hdNumberToGet].Partition[2].Start == "819200" &&
                                        specs.Hd[hdNumberToGet].Partition[3].Start == "1081344")
                                        return "win8gpt";
                                    return "false";

                                default:
                                    return "false";
                            }

                        default:
                            return "Error: Could Not Determine Partition Table Type";
                    }
            }

            return "false";
        }

        public string GetMinHdSize(string imgName, string hdToGet, string newHdSize)
        {
            var image = new Image {Name = imgName};
            image.Read();
            ImagePhysicalSpecs specs;
            if (!string.IsNullOrEmpty(image.ClientSizeCustom))
            {
                specs = JsonConvert.DeserializeObject<ImagePhysicalSpecs>(image.ClientSizeCustom);
                try
                {
                    specs = JsonConvert.DeserializeObject<ImagePhysicalSpecs>(image.ClientSizeCustom);
                }
                catch
                {
                    // ignored
                }
            }
            else
            {
                specs = JsonConvert.DeserializeObject<ImagePhysicalSpecs>(image.ClientSize);
                try
                {
                    specs = JsonConvert.DeserializeObject<ImagePhysicalSpecs>(image.ClientSize);
                }
                catch
                {
                    // ignored
                }
            }


            if (specs.Hd == null)
            {
                return "compatibility,0";
            }

            if (specs.Hd.Count() < Convert.ToInt32(hdToGet))
            {
                return "notexist,0";
            }

            var activeCounter = Convert.ToInt32(hdToGet);
            var foundActive = false;
            var hdNumberToGet = Convert.ToInt32(hdToGet) - 1;

            //Look for first active hd
            if (specs.Hd[hdNumberToGet].Active == "1")
            {
                foundActive = true;
            }
            else
            {
                while (activeCounter <= specs.Hd.Count())
                {
                    if (specs.Hd[activeCounter - 1].Active == "1")
                    {
                        hdNumberToGet = activeCounter - 1;
                        foundActive = true;
                        break;
                    }
                    activeCounter++;
                }
            }

            if (!foundActive)
            {
                return "notactive,0";
            }
            var minimumSize = new MinimumSize {Image = image};
            var minHdSizeRequiredByte = minimumSize.Hd(hdNumberToGet, newHdSize);
            var newHdBytes = Convert.ToInt64(newHdSize);

            if (minHdSizeRequiredByte > newHdBytes)
            {
                Logger.Log("Error:  " + newHdBytes/1024/1024 +
                           " MB Is Less Than The Minimum Required HD Size For This Image(" +
                           minHdSizeRequiredByte/1024/1024 + " MB)");

                return "false," + (hdNumberToGet + 1);
            }
            if (minHdSizeRequiredByte == newHdBytes)
            {
                return "original," + (hdNumberToGet + 1);
            }
            return "true," + (hdNumberToGet + 1);
        }

        public string GetOriginalLvm(string imgName, string clienthd, string hdToGet)
        {
            string result = null;
            var image = new Image {Name = imgName};
            image.Read();
            var specs =
                JsonConvert.DeserializeObject<ImagePhysicalSpecs>(!string.IsNullOrEmpty(image.ClientSizeCustom)
                    ? image.ClientSizeCustom
                    : image.ClientSize);
            var hdNumberToGet = Convert.ToInt32(hdToGet) - 1;

            foreach (var part in from part in specs.Hd[hdNumberToGet].Partition
                where part.Active == "1"
                where part.Vg != null
                where part.Vg.Lv != null
                select part)
            {
                result = "pvcreate -u " + part.Uuid + " --norestorefile -yf " +
                         clienthd + part.Vg.Pv[part.Vg.Pv.Length - 1] + "\r\n";
                result += "vgcreate " + part.Vg.Name + " " + clienthd +
                          part.Vg.Pv[part.Vg.Pv.Length - 1] + " -yf" + "\r\n";
                result += "echo \"" + part.Vg.Uuid + "\" >>/tmp/vg-" + part.Vg.Name +
                          "\r\n";
                foreach (var lv in part.Vg.Lv.Where(lv => lv.Active == "1"))
                {
                    result += "lvcreate -L " + lv.Size + "s -n " + lv.Name + " " +
                              lv.Vg + "\r\n";
                    result += "echo \"" + lv.Uuid + "\" >>/tmp/" + lv.Vg + "-" +
                              lv.Name + "\r\n";
                }
                result += "vgcfgbackup -f /tmp/lvm-" + part.Vg.Name + "\r\n";
            }

            return result;
        }

        public string InSlot(string mac)
        {
            using (var db = new DB())
            {
                var q = (from h in db.Hosts
                         join t in db.ActiveTasks on h.Name equals t.Name
                         where (h.Mac.ToLower() == mac.ToLower())
                         select t).FirstOrDefault();
                q.Status = "3";
                return q.Update("status") ? "Success" : "";

            }

           
        }

        public string ModifyKnownLayout(string layout, string clientHd)
        {
            string partCommands;
            switch (layout)
            {
                case "winxp":
                    partCommands = "fdisk -c=dos " + clientHd + " &>>/tmp/clientlog.log <<FDISK\r\n";
                    partCommands += "d\r\n";
                    partCommands += "n\r\n";
                    partCommands += "p\r\n";
                    partCommands += "1\r\n";
                    partCommands += "63\r\n";
                    partCommands += "\r\n";
                    partCommands += "t\r\n";
                    partCommands += "7\r\n";
                    partCommands += "a\r\n";
                    partCommands += "1\r\n";
                    partCommands += "w\r\n";
                    partCommands += "FDISK";
                    break;
                case "winvista":
                    partCommands = "fdisk " + clientHd + " &>>/tmp/clientlog.log <<FDISK\r\n";
                    partCommands += "d\r\n";
                    partCommands += "n\r\n";
                    partCommands += "p\r\n";
                    partCommands += "1\r\n";
                    partCommands += "2048\r\n";
                    partCommands += "\r\n";
                    partCommands += "t\r\n";
                    partCommands += "7\r\n";
                    partCommands += "a\r\n";
                    partCommands += "1\r\n";
                    partCommands += "w\r\n";
                    partCommands += "FDISK";
                    break;
                case "win7":
                    partCommands = "fdisk " + clientHd + " &>>/tmp/clientlog.log <<FDISK\r\n";
                    partCommands += "d\r\n";
                    partCommands += "2\r\n";
                    partCommands += "n\r\n";
                    partCommands += "p\r\n";
                    partCommands += "2\r\n";
                    partCommands += "206848\r\n";
                    partCommands += "\r\n";
                    partCommands += "t\r\n";
                    partCommands += "2\r\n";
                    partCommands += "7\r\n";
                    partCommands += "w\r\n";
                    partCommands += "FDISK";
                    break;
                case "win8":
                    partCommands = "fdisk " + clientHd + " &>>/tmp/clientlog.log <<FDISK\r\n";
                    partCommands += "d\r\n";
                    partCommands += "2\r\n";
                    partCommands += "n\r\n";
                    partCommands += "p\r\n";
                    partCommands += "2\r\n";
                    partCommands += "718848\r\n";
                    partCommands += "\r\n";
                    partCommands += "t\r\n";
                    partCommands += "2\r\n";
                    partCommands += "7\r\n";
                    partCommands += "w\r\n";
                    partCommands += "FDISK";
                    break;
                case "win7gpt":
                    partCommands = "gdisk " + clientHd + " &>>/tmp/clientlog.log <<GDISK\r\n";
                    partCommands += "d\r\n";
                    partCommands += "3\r\n";
                    partCommands += "n\r\n";
                    partCommands += "3\r\n";
                    partCommands += "\r\n";
                    partCommands += "\r\n";
                    partCommands += "0700\r\n";
                    partCommands += "w\r\n";
                    partCommands += "Y\r\n";
                    partCommands += "GDISK";
                    break;
                case "win8gpt":
                    partCommands = "gdisk " + clientHd + " &>>/tmp/clientlog.log <<GDISK\r\n";
                    partCommands += "d\r\n";
                    partCommands += "4\r\n";
                    partCommands += "n\r\n";
                    partCommands += "4\r\n";
                    partCommands += "\r\n";
                    partCommands += "\r\n";
                    partCommands += "0700\r\n";
                    partCommands += "w\r\n";
                    partCommands += "Y\r\n";
                    partCommands += "GDISK";
                    break;
                default:
                    partCommands = "false";
                    break;
            }

            return partCommands;
        }

        public string MulticastCheckout(string portBase)
        {
            string result;
            ActiveMcTask mcTask;
            using (var db = new DB())
            {
                mcTask = db.ActiveMcTasks.FirstOrDefault(t => t.Port == portBase);
            }
        
            if (mcTask != null)
            {
                var prsRunning = true;

                if (Environment.OSVersion.ToString().Contains("Unix"))
                {
                    try
                    {
                        var prs = Process.GetProcessById(Convert.ToInt32(mcTask.Pid));
                        if (prs.HasExited)
                        {
                            prsRunning = false;
                        }
                    }
                    catch
                    {
                        prsRunning = false;
                    }
                }
                else
                {
                    try
                    {
                        Process.GetProcessById(Convert.ToInt32(mcTask.Pid));
                    }
                    catch
                    {
                        prsRunning = false;
                    }
                }
                if (!prsRunning)
                {
                    using (var db = new DB())
                    {
                        db.ActiveMcTasks.Remove(mcTask);
                        db.SaveChanges();
                        result = "Success";
                    }
                }
                else
                    result = "Cannot Close Session, It Is Still In Progress";
            }
            else
                result = "Session Is Already Closed";
            return result;
        }

        public string QueuePosition(string mac)
        {
            string result;
            using (var db = new DB())
            {
                var maxQueuePosition = (from t in db.ActiveTasks
                                        where t.Status == "2" && t.Type == "unicast"
                                        orderby t.QueuePosition descending
                                        select t.QueuePosition).FirstOrDefault();


                var q = (from h in db.Hosts
                         join t in db.ActiveTasks on h.Name equals t.Name
                         where (h.Mac.ToLower() == mac.ToLower())
                         select t).FirstOrDefault();
                q.Status = "2";
                q.QueuePosition = (Convert.ToInt16(maxQueuePosition) + 1);
                result = q.QueuePosition.ToString();
                q.Update("task");
            }

            return result;
        }

        public string QueueStatus()
        {
    
            string result;

            using (var db = new DB())
            {
                result = db.ActiveTasks.Count(t => t.Status == "3" && t.Type == "unicast").ToString();
            }

            return result + "," + Settings.QueueSize;
        }
    }
}