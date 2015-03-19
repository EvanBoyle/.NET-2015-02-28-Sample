﻿using Microsoft.Azure;
using Microsoft.Azure.Search;
using Microsoft.Azure.Search.Models;
using Microsoft.Spatial;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

namespace DataIndexer
{
    class Program
    {
        private static SearchServiceClient _searchClient;
        private static SearchIndexClient _indexClient;

        // This Sample shows how to delete, create, upload documents and query an index
        static void Main(string[] args)
        {
            string searchServiceName = ConfigurationManager.AppSettings["SearchServiceName"];
            string apiKey = ConfigurationManager.AppSettings["SearchServiceApiKey"];

            // Create an HTTP reference to the catalog index
            _searchClient = new SearchServiceClient(searchServiceName, new SearchCredentials(apiKey));
            _indexClient = _searchClient.Indexes.GetClient("features");

            Console.WriteLine("{0}", "Deleting index...\n");
            if (DeleteIndex())
            {
                Console.WriteLine("{0}", "Creating index...\n");
                CreateIndex();
                Console.WriteLine("{0}", "Sync documents from Azure SQL...\n");
                SyncDataFromAzureSQL();
            }
            Console.WriteLine("{0}", "Complete.  Press any key to end application...\n");
            Console.ReadKey();
        }

        private static bool DeleteIndex()
        {
            // Delete the index if it exists
            try
            {
                AzureOperationResponse response = _searchClient.Indexes.Delete("features");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error deleting index: {0}\r\n", ex.Message.ToString());
                Console.WriteLine("Did you remember to add your SearchServiceName and SearchServiceApiKey to the app.config?\r\n");
                return false;
            }

            return true;
        }

        private static void CreateIndex()
        {
            // Create the Azure Search index based on the included schema
            try
            {
                var definition = new Index()
                {
                    Name = "features",
                    Fields = new[] 
                    { 
                        new Field("FEATURE_ID",     DataType.String)         { IsKey = true,  IsSearchable = false, IsFilterable = false, IsSortable = false, IsFacetable = false, IsRetrievable = true},
                        new Field("FEATURE_NAME",   DataType.String)         { IsKey = false, IsSearchable = true,  IsFilterable = true,  IsSortable = true,  IsFacetable = false, IsRetrievable = true},
                        new Field("FEATURE_CLASS",  DataType.String)         { IsKey = false, IsSearchable = true,  IsFilterable = true,  IsSortable = true,  IsFacetable = false, IsRetrievable = true},
                        new Field("STATE_ALPHA",    DataType.String)         { IsKey = false, IsSearchable = true,  IsFilterable = true,  IsSortable = true,  IsFacetable = false, IsRetrievable = true},
                        new Field("STATE_NUMERIC",  DataType.Int32)          { IsKey = false, IsSearchable = false, IsFilterable = true,  IsSortable = true,  IsFacetable = true,  IsRetrievable = true},
                        new Field("COUNTY_NAME",    DataType.String)         { IsKey = false, IsSearchable = true,  IsFilterable = true,  IsSortable = true,  IsFacetable = false, IsRetrievable = true},
                        new Field("COUNTY_NUMERIC", DataType.Int32)          { IsKey = false, IsSearchable = false, IsFilterable = true,  IsSortable = true,  IsFacetable = true,  IsRetrievable = true},
                        new Field("LOCATION",       DataType.GeographyPoint) { IsKey = false, IsSearchable = false, IsFilterable = true,  IsSortable = true,  IsFacetable = false, IsRetrievable = true},
                        new Field("ELEV_IN_M",      DataType.Int32)          { IsKey = false, IsSearchable = false, IsFilterable = true,  IsSortable = true,  IsFacetable = true,  IsRetrievable = true},
                        new Field("ELEV_IN_FT",     DataType.Int32)          { IsKey = false, IsSearchable = false, IsFilterable = true,  IsSortable = true,  IsFacetable = true,  IsRetrievable = true},
                        new Field("MAP_NAME",       DataType.String)         { IsKey = false, IsSearchable = true,  IsFilterable = true,  IsSortable = true,  IsFacetable = false, IsRetrievable = true},
                        new Field("DESCRIPTION",    DataType.String)         { IsKey = false, IsSearchable = true,  IsFilterable = false, IsSortable = false, IsFacetable = false, IsRetrievable = true},
                        new Field("HISTORY",        DataType.String)         { IsKey = false, IsSearchable = true,  IsFilterable = false, IsSortable = false, IsFacetable = false, IsRetrievable = true},
                        new Field("DATE_CREATED",   DataType.DateTimeOffset) { IsKey = false, IsSearchable = false, IsFilterable = true,  IsSortable = true,  IsFacetable = true,  IsRetrievable = true},
                        new Field("DATE_EDITED",    DataType.DateTimeOffset) { IsKey = false, IsSearchable = false, IsFilterable = true,  IsSortable = true,  IsFacetable = true,  IsRetrievable = true}
                    }
                };

                _searchClient.Indexes.Create(definition);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error creating index: {0}\r\n", ex.Message.ToString());
            }

        }

        private static bool SyncDataFromAzureSQL()
        {
            // This will use the Azure Search Indexer to synchronize data from Azure SQL to Azure Search
            Uri _serviceUri = new Uri("https://" + ConfigurationManager.AppSettings["SearchServiceName"] + ".search.windows.net");
            HttpClient _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("api-key", ConfigurationManager.AppSettings["SearchServiceApiKey"]);

            Console.WriteLine("{0}", "Creating Indexer Data Source...\n");
            Uri uri = new Uri(_serviceUri, "datasources/usgs-datasource");
            string json = "{ 'name' : 'usgs-datasource','description' : 'USGS Dataset','type' : 'azuresql','credentials' : { 'connectionString' : 'Server=tcp:azs-playground.database.windows.net,1433;Database=usgs;User ID=reader;Password=Search42;Trusted_Connection=False;Encrypt=True;Connection Timeout=30;' },'container' : { 'name' : 'GeoNamesRI' }} ";
            HttpResponseMessage response = AzureSearchHelper.SendSearchRequest(_httpClient, HttpMethod.Put, uri, json);
            if (response.StatusCode != HttpStatusCode.Created)
                return false;

            Console.WriteLine("{0}", "Creating Indexer...\n");
            uri = new Uri(_serviceUri, "indexers/usgs-indexer");
            json = "{ 'name' : 'usgs-indexer','description' : 'USGS data indexer','dataSourceName' : 'usgs-datasource','targetIndexName' : 'geonames','parameters' : { 'maxFailedItems' : 10, 'maxFailedItemsPerBatch' : 5, 'base64EncodeKeys': false }}";
            response = AzureSearchHelper.SendSearchRequest(_httpClient, HttpMethod.Put, uri, json);
            if (response.StatusCode != HttpStatusCode.Created)
                return false;

            Console.WriteLine("{0}", "Syncing data...\n");
            uri = new Uri(_serviceUri, "indexers/usgs-indexer/run");
            response = AzureSearchHelper.SendSearchRequest(_httpClient, HttpMethod.Post, uri);
            if (response.StatusCode != HttpStatusCode.Accepted)
                return false;

            bool running = true;
            Console.WriteLine("{0}", "Synchronization running...\n");
            while (running)
            {
                uri = new Uri(_serviceUri, "indexers/usgs-indexer/status");
                response = AzureSearchHelper.SendSearchRequest(_httpClient, HttpMethod.Get, uri);
                if (response.StatusCode != HttpStatusCode.OK)
                    return false;

                var result = AzureSearchHelper.DeserializeJson<dynamic>(response.Content.ReadAsStringAsync().Result);
                if (result.lastResult != null)
                {
                    if (result.lastResult.status.Value == "inProgress")
                    {
                        Console.WriteLine("{0}", "Synchronization running...\n");
                        Thread.Sleep(1000);
                    }
                    else
                    {
                        running = false;
                        Console.WriteLine("Synchronized {0} rows...\n", result.lastResult.itemsProcessed.Value);
                    }
                }
            }

            return true;
        }

        private static void SearchDocuments(string q, string filter)
        {
            // Execute search based on query string (q) and filter 
            try
            {
                SearchParameters sp = new SearchParameters();
                if (filter != string.Empty)
                    sp.Filter = filter;
                DocumentSearchResponse response = _indexClient.Documents.Search(q, sp);
                foreach (SearchResult doc in response)
                {
                    string StoreName = doc.Document["StoreName"].ToString();
                    string Address = (doc.Document["AddressLine1"].ToString() + " " + doc.Document["AddressLine2"].ToString()).Trim();
                    string City = doc.Document["City"].ToString();
                    string Country = doc.Document["Country"].ToString();
                    Console.WriteLine("Store: {0}, Address: {1}, {2}, {3}", StoreName, Address, City, Country);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error querying index: {0}\r\n", ex.Message.ToString());
            }
        }

        static string EscapeQuotes(string colVal)
        {
            return colVal.Replace("'", "");
        }


    }
}
