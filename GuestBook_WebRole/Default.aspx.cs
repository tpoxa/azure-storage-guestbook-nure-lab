﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.UI;
using System.Web.UI.WebControls;
using System.IO;
using System.Net;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.ServiceRuntime;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;
using GuestBook_Data;

namespace GuestBook_WebRole
{
    public partial class _Default : System.Web.UI.Page
    {
        private static bool storageInitialized = false;
        private static object gate = new object();
        private static CloudBlobClient blobStorage;
        private static CloudQueueClient queueStorage;

        protected void Page_Load(object sender, EventArgs e)
        {
            if (!Page.IsPostBack)
            {
                this.Timer1.Enabled = true;
            }
        }

        protected void SignButton_Click(object sender, EventArgs e)
        {
            if (this.FileUpload1.HasFile)
            {
                this.InitializeStorage();

                // upload the image to blob storage
                string uniqueBlobName = string.Format("guestbookpics/image_{0}{1}", Guid.NewGuid().ToString(), Path.GetExtension(this.FileUpload1.FileName));
                CloudBlockBlob blob = blobStorage.GetContainerReference("guestbookpics").GetBlockBlobReference(uniqueBlobName);
                blob.Properties.ContentType = this.FileUpload1.PostedFile.ContentType;
                blob.UploadFromStream(this.FileUpload1.FileContent);
                System.Diagnostics.Trace.TraceInformation("Uploaded image '{0}' to blob storage as '{1}'", this.FileUpload1.FileName, uniqueBlobName);

                // create a new entry in table storage
                GuestBookEntry entry = new GuestBookEntry() { GuestName = this.NameTextBox.Text, Message = this.MessageTextBox.Text, PhotoUrl = blob.Uri.ToString(), ThumbnailUrl = blob.Uri.ToString() };
                GuestBookDataSource ds = new GuestBookDataSource();
                ds.AddGuestBookEntry(entry);
                System.Diagnostics.Trace.TraceInformation("Added entry {0}-{1} in table storage for guest '{2}'", entry.PartitionKey, entry.RowKey, entry.GuestName);

                // queue a message to process the image
                var queue = queueStorage.GetQueueReference("guestthumbs");
                var message = new CloudQueueMessage(string.Format("{0},{1},{2}", uniqueBlobName, entry.PartitionKey, entry.RowKey));
                queue.AddMessage(message);
                System.Diagnostics.Trace.TraceInformation("Queued message to process blob '{0}'", uniqueBlobName);
            }

            this.NameTextBox.Text = string.Empty;
            this.MessageTextBox.Text = string.Empty;

            this.DataList1.DataBind();
        }

        protected void Timer1_Tick(object sender, EventArgs e)
        {
            this.DataList1.DataBind();
        }

        private void InitializeStorage()
        {
            if (storageInitialized)
            {
                return;
            }

            lock (gate)
            {
                if (storageInitialized)
                {
                    return;
                }

                try
                {
                    // read account configuration settings
                    var storageAccount = CloudStorageAccount.Parse(CloudConfigurationManager.GetSetting("DataConnectionString"));

                    // create blob container for images
                    blobStorage = storageAccount.CreateCloudBlobClient();
                    CloudBlobContainer container = blobStorage.GetContainerReference("guestbookpics");
                    container.CreateIfNotExists();

                    // configure container for public access
                    var permissions = container.GetPermissions();
                    permissions.PublicAccess = BlobContainerPublicAccessType.Container;
                    container.SetPermissions(permissions);

                    // create queue to communicate with worker role
                    queueStorage = storageAccount.CreateCloudQueueClient();
                    CloudQueue queue = queueStorage.GetQueueReference("guestthumbs");
                    queue.CreateIfNotExists();
                }
                catch (WebException)
                {
                    throw new WebException("Storage services initialization failure. "
                       + "Check your storage account configuration settings. If running locally, "
                       + "ensure that the Development Storage service is running.");
                }

                storageInitialized = true;
            }
        }
    }
}
