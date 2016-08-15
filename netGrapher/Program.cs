using System;
using System.Data;
using System.Linq;
using System.Xml;
using System.Windows.Forms.DataVisualization.Charting;

namespace netGrapher
{
    class Program
    {
        public static void Main(string[] args)
        {
        Start:
            // Temp before settings is implemented
            string[] counters = { "Cisco SIP", "Cisco MGCP Gateways", "Cisco MGCP PRI Device" };
            string[] servers = { "127.0.0.1", "127.0.0.2" };

            // Create delimiters, devicearray, devicestring, devicevalue before foreach so that ContainsKey works
            var delimiters = new char[] { '(', ')' };
            string devicestring = null;
            int devicevalue = 0;
            // A Dictionary<string, int> to aggregate the traffic info in
            var result = new System.Collections.Generic.Dictionary<string, int>();
            // loop through all counters and servers and save the result in result<string,int>
            foreach (var counter in counters)
            {
                foreach (var server in servers)
                {
                    try
                    {
                        // Using is used to dispose of the resource after it has been used or something happens( runs dispose method)
                        using (var client = new System.Net.WebClient())
                        {
                            //var url = string.Format(@"http://{0}:443/perfmonservice/services/PerfmonPort",server); // Use against CUCM
                            var url = string.Format(@"http://{0}/Temporary_Listen_Addresses/", server); // Use agains simulation server
                            client.Headers.Add("SOAPAction", "perfmonCollectCounterData");
                            var response = client.UploadString(url, "POST", counter);
                            var xmlResponse = new XmlDocument();
                            xmlResponse.LoadXml(response.ToString());

                            // Finds all "item"-tags in the response and loops through them so that we can deal with the value
                            XmlNodeList nodelist = xmlResponse.GetElementsByTagName("item");

                            foreach (XmlNode node in nodelist)
                            {
                                switch (counter)
                                {
                                    // We only want the value in CallsInProgress, PRIChannelsActive or CallsActive so we filter those here.
                                    case "Cisco SIP":
                                        devicestring = System.Text.RegularExpressions.Regex.Match(node["Name"].InnerXml, @"^.*Cisco SIP\((.*)\)\\CallsInProgress$").Groups[1].Value;
                                        break;
                                    case "Cisco MGCP Gateways":
                                        devicestring = System.Text.RegularExpressions.Regex.Match(node["Name"].InnerXml, @"^.*Cisco MGCP Gateways\((.*)\)\\PRIChannelsActive$").Groups[1].Value;
                                        break;
                                    case "Cisco MGCP PRI Device":
                                        devicestring = System.Text.RegularExpressions.Regex.Match(node["Name"].InnerXml, @"^.*Cisco MGCP PRI Device\((.*)\)\\CallsActive$").Groups[1].Value;
                                        break;
                                    default:
                                        throw new Exception("Invalid counter: " + counter);
                                }

                                devicevalue = Convert.ToInt32(node["Value"].InnerXml);

                                // If the regex doesnt match devicestring will be empty. We do not want to add an empty devicestring in the dict.
                                if (devicestring != "")
                                {
                                    if (result.ContainsKey(devicestring))
                                    {
                                        Console.WriteLine("Uppdating: " + devicestring + " " + devicevalue);
                                        result[devicestring] += devicevalue;
                                    }
                                    else
                                    {
                                        Console.WriteLine("Adding: " + devicestring + " " + devicevalue);
                                        result.Add(devicestring, devicevalue);
                                    }
                                }
                            }
                        }
                    }
                    catch (System.Net.WebException e)
                    {

                        Console.WriteLine(e.GetType() + " " + e.Message + " " + server);
                    }
                }
            }

            // Do something useful with the result
            //
            //foreach (var item in result)
            //{
            //}

            // Create a dataset to save the data
            var ds = new System.Data.DataSet();
            // Try to read a previous databasefile
            try
            {
                Console.WriteLine("Loading database.xml");
                ds.ReadXml("database.xml");
            }
            catch (System.IO.FileNotFoundException e)
            {
                // And if that doesnt exist create a new one.
                Console.WriteLine(e.GetType() + " " + e.Message);
                Console.WriteLine("Creating a new database.xml");
            }
            catch (System.Xml.XmlException e)
            {
                // or if it is broken make backup and make new.
                Console.WriteLine(e.GetType() + " " + e.Message);
                Console.WriteLine("database.xml is broken so I am saving a backup and creating a new one");
                System.IO.File.Move("database.xml", "database.xml.broken");

            }

            // Loop through result-dict and add it to the dataset
            foreach (var item in result)
            {
                // Check if the DS contains a table with the device as key
                if (ds.Tables.Contains(item.Key))
                {
                    // If it does, add the result to it
                    var dr = ds.Tables[item.Key].NewRow();
                    dr[0] = DateTime.Now;
                    dr[1] = (int)item.Value;
                    //dr[2] = ds.Tables[item.Key].Compute("Max(Value)", String.Empty); <--- Old way
                    dr[2] = ds.Tables[item.Key].AsEnumerable().Max(r => Convert.ToInt32(r.Field<string>("Value"))); // <---- New way
                    ds.Tables[item.Key].Rows.Add(dr);
                }
                else
                {
                    // If it doesnt, create it and add the data
                    var dt = new System.Data.DataTable(item.Key.ToString());
                    dt.Columns.Add("Timestamp", typeof(DateTime));
                    dt.Columns.Add("Value", typeof(int));
                    dt.Columns.Add("Peak", typeof(int));
                    var dr = dt.NewRow();
                    dr[0] = DateTime.Now;
                    dr[1] = (int)item.Value;
                    dr[2] = dr[1]; // Since it is a new table Value and Peak will be the same
                    dt.Rows.Add(dr);
                    ds.Tables.Add(dt);
                }
            }
            // Save the database for future use.
            ds.WriteXml("database.xml");

            // Create a chart for each table in the dataset
            foreach (System.Data.DataTable table in ds.Tables)
            {
                var chart = new Chart();
                chart.DataSource = table;
                chart.Width = 1200;
                chart.Height = 400;
                chart.Titles.Add(table.TableName.ToString());

                // Create a series for the chart
                var serie = new Series();
                serie.Name = "Calls";
                serie.ChartType = SeriesChartType.SplineArea;
                serie.Color = System.Drawing.Color.Green;
                serie.BorderColor = System.Drawing.Color.DarkGreen;
                serie.BorderWidth = 4;
                serie.IsValueShownAsLabel = false;
                serie.XValueMember = "Timestamp";
                serie.YValueMembers = "Value";

                // Create a Peak-value series. This will always lag one update behind
                var serieMax = new Series();
                serieMax.Name = "Peak";
                serieMax.ChartType = SeriesChartType.Line;
                serieMax.Color = System.Drawing.Color.Red;
                serieMax.BorderColor = System.Drawing.Color.DarkRed;
                serieMax.BorderWidth = 4;
                serieMax.IsValueShownAsLabel = false;
                serieMax.XValueMember = "Timestamp";
                serieMax.YValueMembers = "Peak";
                // Add the series
                chart.Series.Add(serieMax);
                chart.Series.Add(serie);

                // Create a chart area
                var ca = new ChartArea();
                ca.Name = table.ToString();
                //ca.BackColor = Color.White;
                //ca.BorderColor = System.Drawing.Color.Black;
                //ca.BorderWidth = 0;
                //ca.BorderDashStyle = ChartDashStyle.Solid;
                ca.AxisX = new Axis();
                ca.AxisY = new Axis();
                ca.AxisX.IsMarginVisible = false;
                //ca.AxisX.ScaleView.Size = 10;
                ca.AxisY.Interval = 10;
                //ca.AxisX.IsReversed = true;
                //ca.AxisX.Maximum = 288;
                ca.AxisX.Interval = 0;
                chart.ChartAreas.Add(ca);

                // Create a legend for the chart
                var legend = new Legend();
                legend.IsTextAutoFit = true;
                chart.Legends.Add(legend);

                // Databind. Do not know what this does but internet said it had to be here
                chart.DataBind();

                // Save as PNG
                chart.SaveImage(table.ToString() + ".png", ChartImageFormat.Png);
            }


            // The last that happens. Should be removed/tweaked before release
            Console.WriteLine("Done. Sover i 10 och kör igen");
            System.Threading.Thread.Sleep(10000);
            goto Start;
            //Console.ReadKey(true);

        }
    }
}