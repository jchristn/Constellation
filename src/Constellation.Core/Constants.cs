namespace Constellation.Core
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Constants.
    /// </summary>
    public static class Constants
    {
        #region General

        /// <summary>
        /// Logo.
        /// </summary>
        public static string Logo =
            @"                 _       _ _      _   _           " + Environment.NewLine +
            @"  __ ___ _ _  __| |_ ___| | |__ _| |_(_)___ _ _   " + Environment.NewLine +
            @" / _/ _ \ ' \(_-<  _/ -_) | / _` |  _| / _ \ ' \  " + Environment.NewLine +
            @" \__\___/_||_/__/\__\___|_|_\__,_|\__|_\___/_||_| " + Environment.NewLine;
                                                 

        /// <summary>
        /// Timestamp format.
        /// </summary>
        public static string TimestampFormat = "yyyy-MM-ddTHH:mm:ss.ffffffZ";

        #endregion

        #region Node

        /// <summary>
        /// Settings file.
        /// </summary>
        public static string SettingsFile = "./constellation.json";

        /// <summary>
        /// Database filename.
        /// </summary>
        public static string DatabaseFile = "./constellation.db";

        /// <summary>
        /// Log filename.
        /// </summary>
        public static string LogFilename = "./constellation.log";

        /// <summary>
        /// Log directory.
        /// </summary>
        public static string LogDirectory = "./logs/";

        /// <summary>
        /// Copyright.
        /// </summary>
        public static string Copyright = "(c)2025 Joel Christner";

        #endregion

        #region Headers

        /// <summary>
        /// Authorization header.
        /// </summary>
        public static string AuthorizationHeader = "authorization";

        /// <summary>
        /// Worker name header.
        /// </summary>
        public static string WorkerNameHeader = "x-worker";

        /// <summary>
        /// Request GUID header.
        /// </summary>
        public static string RequestGuidHeader = "x-request";

        /// <summary>
        /// Forwarded for header, generally x-forwarded-for.
        /// </summary>
        public static string ForwardedForHeader = "x-forwarded-for";

        #endregion

        #region Content-Types

        /// <summary>
        /// Binary content type.
        /// </summary>
        public static string BinaryContentType = "application/octet-stream";

        /// <summary>
        /// JSON content type.
        /// </summary>
        public static string JsonContentType = "application/json";

        /// <summary>
        /// HTML content type.
        /// </summary>
        public static string HtmlContentType = "text/html";

        /// <summary>
        /// PNG content type.
        /// </summary>
        public static string PngContentType = "image/png";

        /// <summary>
        /// Text content type.
        /// </summary>
        public static string TextContentType = "text/plain";

        /// <summary>
        /// Favicon filename.
        /// </summary>
        public static string FaviconFilename = "assets/favicon.png";

        /// <summary>
        /// Favicon content type.
        /// </summary>
        public static string FaviconContentType = "image/png";

        #endregion

        #region Default-Homepage

        /// <summary>
        /// Default HTML homepage.
        /// </summary>
        public static string HtmlHomepage =
            @"<html>" + Environment.NewLine +
            @"  <head>" + Environment.NewLine +
            @"    <title>Node is Operational</title>" + Environment.NewLine +
            @"  </head>" + Environment.NewLine +
            @"  <body>" + Environment.NewLine +
            @"    <div>" + Environment.NewLine +
            @"      <pre>" + Environment.NewLine + Environment.NewLine +
            Logo + Environment.NewLine +
            @"      </pre>" + Environment.NewLine +
            @"    </div>" + Environment.NewLine +
            @"    <div style='font-family: Arial, sans-serif;'>" + Environment.NewLine +
            @"      <h2>Your node is operational</h2>" + Environment.NewLine +
            @"      <p>Congratulations, your node is operational.  Please refer to the documentation for use.</p>" + Environment.NewLine +
            @"    <div>" + Environment.NewLine +
            @"  </body>" + Environment.NewLine +
            @"</html>" + Environment.NewLine;

        #endregion
    }
}
