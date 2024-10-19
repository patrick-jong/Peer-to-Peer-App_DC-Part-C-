using Library_DLL;
using IronPython.Hosting;
using Microsoft.Scripting.Hosting;
using Newtonsoft.Json;
using RestSharp;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.ServiceModel;
using System.Threading;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Text;
using System.Security.Cryptography;
using System.IO;
using System.Net;
using System.Threading.Tasks;

namespace ClientDesktopApp
{
    public partial class MainWindow : Window
    {
        ServiceHost host;
        private JobServerInterface foob;
        private int port;
        private int clientID;
        private int totalJobsCompleted = 0;
        private string currJobProgress = "Idle";
        private bool isClosed = false;
        private RestClient restClient;

        public MainWindow()
        {
            InitializeComponent();
            restClient = new RestClient("http://localhost:5000");
        }

        // Function for Server thread to host .NET Remoting service
        private void Server()
        {
            NetTcpBinding tcp = new NetTcpBinding();
            host = new ServiceHost(typeof(JobServer));

            host.AddServiceEndpoint(typeof(JobServerInterface), tcp, "net.tcp://localhost:" + port + "/JobService");
            host.Open();
            Console.WriteLine("System Online");
            Console.ReadLine();

            while (!isClosed) { }

            host.Close();
        }

        // Function for Network thread to look for new clients and check for jobs to do
        private void Network()
        {
            RestRequest restRequest = new RestRequest("/api/clients", Method.Get);
            RestResponse restResponse = restClient.Execute(restRequest);

            IEnumerable<Client> clients = JsonConvert.DeserializeObject<IEnumerable<Client>>(restResponse.Content);

            while (true)
            {
                // Check if the current client has any completed jobs
                CheckForCompletedJobs();

                foreach (var client in clients)
                {
                    if (client.Port != port)
                    {
                        try
                        {
                            ChannelFactory<JobServerInterface> foobFactory;
                            NetTcpBinding tcp = new NetTcpBinding();

                            string URL = "net.tcp://localhost:" + client.Port + "/JobService";
                            foobFactory = new ChannelFactory<JobServerInterface>(tcp, URL);
                            foob = foobFactory.CreateChannel();

                            Job job = foob.RequestJob();

                            if (job != null)
                            {
                                // Update job progress status to "In Progress"
                                currJobProgress = "In Progress";
                                UpdateClient();

                                // Update the GUI to reflect job progress
                                Dispatcher.Invoke(() =>
                                {
                                    ProgressLbl.Foreground = Brushes.Red;
                                    ProgressLbl.Content = currJobProgress;
                                });

                                byte[] encodedString = Convert.FromBase64String(job.PythonScript);

                                // Check hash to ensure job integrity
                                SHA256 sha256hASH = SHA256.Create();
                                byte[] hash = sha256hASH.ComputeHash(encodedString);

                                if (hash.SequenceEqual(job.Hash))
                                {
                                    // Execute the Python script
                                    String pythonScript = Encoding.UTF8.GetString(encodedString);
                                    var result = RunPythonScript(pythonScript);
                                    string sResult = result.ToString();

                                    // Delay to simulate job execution time
                                    Thread.Sleep(1000);

                                    // Submit the job result back to the server
                                    foob.SubmitResult(job.JobId, sResult);

                                    // Update job progress to "Completed"
                                    currJobProgress = "Completed";
                                    UpdateClient();

                                    // Update GUI to reflect completion status
                                    Dispatcher.Invoke(() =>
                                    {
                                        ProgressLbl.Foreground = Brushes.Green;
                                        ProgressLbl.Content = currJobProgress;
                                    });

                                    // Delay before resetting status
                                    Thread.Sleep(1000);

                                    // Increment the number of jobs completed
                                    totalJobsCompleted++;
                                    currJobProgress = "Idle";
                                    UpdateClient();

                                    // Update the total jobs completed in the GUI
                                    Dispatcher.Invoke(() =>
                                    {
                                        TotalLbl.Content = totalJobsCompleted.ToString(); // Update the total jobs completed label
                                    });
                                }
                            }
                        }
                        catch (TaskCanceledException e)
                        {
                            // Retry getting the client list if the task is canceled
                            restResponse = restClient.Execute(restRequest);
                            clients = JsonConvert.DeserializeObject<IEnumerable<Client>>(restResponse.Content);
                        }
                        catch (Exception e)
                        {
                            // Handle any other errors and update the client list
                            MessageBox.Show(e.Message);
                            restResponse = restClient.Execute(restRequest);
                            clients = JsonConvert.DeserializeObject<IEnumerable<Client>>(restResponse.Content);
                        }
                    }
                }
                // Re-fetch the client list periodically
                restResponse = restClient.Execute(restRequest);
                clients = JsonConvert.DeserializeObject<IEnumerable<Client>>(restResponse.Content);
            }
        }

        // Function to check if the original client (who posted jobs) has any completed jobs
        private void CheckForCompletedJobs()
        {
            foreach (var job in JobList.Jobs)
            {
                if (job.Status == Job.JobStatus.Completed)
                {
                    // The job is completed, update the GUI
                    Dispatcher.Invoke(() =>
                    {
                        Paragraph paragraph = new Paragraph(new Run($"Job {job.JobId} result: {job.Result}"));
                        ResultTB.Document.Blocks.Add(paragraph);
                        ProgressLbl.Foreground = Brushes.DarkGreen;
                        ProgressLbl.Content = "Job Completed";
                    });

                    job.Status = Job.JobStatus.Displayed;
                }
            }
        }

        // Function to run Python scripts
        private dynamic RunPythonScript(string script)
        {
            ScriptEngine engine = Python.CreateEngine();
            ScriptScope scope = engine.CreateScope();

            engine.Execute(script, scope);

            dynamic result = scope.GetVariable("main");

            return result();
        }

        private void RegBtn_Click(object sender, RoutedEventArgs e)
        {
            Register();
        }

        // Function to register the client
        private void Register()
        {
            if (!int.TryParse(PortTB.Text, out port))
            {
                MessageBox.Show("Invalid Port");
                return;
            }

            port = int.Parse(PortTB.Text);

            if (!(port >= 0 && port <= 65535))
            {
                MessageBox.Show("Invalid Port - Must be between 0 and 65535");
                return;
            }

            RestRequest restRequest = new RestRequest("/api/clients", Method.Get);
            RestResponse restResponse = restClient.Execute(restRequest);

            if (restResponse.StatusCode != HttpStatusCode.OK)
            {
                MessageBox.Show("Cannot connect to the Database!");
                return;
            }

            IEnumerable<Client> clients = JsonConvert.DeserializeObject<IEnumerable<Client>>(restResponse.Content);

            if (!clients.Any(c => c.Port == port))
            {
                restRequest = new RestRequest("/api/clients", Method.Post);

                Client client = new Client { IPAddress = "190.1.1.2", Port = port, Status = "Idle", JobsCompleted = 0 };
                restRequest.AddBody(client);

                restResponse = restClient.Execute(restRequest);

                if (restResponse.StatusCode != HttpStatusCode.Created)
                {
                    MessageBox.Show("Cannot connect to the Database!");
                    return;
                }

                clientID = JsonConvert.DeserializeObject<Client>(restResponse.Content).ClientId;

                Thread serverThread = new Thread(new ThreadStart(Server));
                serverThread.Start();

                Thread networkingThread = new Thread(new ThreadStart(Network));
                networkingThread.Start();

                RegPanel.Visibility = Visibility.Hidden;
                MainPanel.Visibility = Visibility.Visible;

                Title = port.ToString();
            }
            else
            {
                MessageBox.Show("Port not available");
            }
        }

        private void PostBtn_Click(object sender, RoutedEventArgs e)
        {
            Job job = new Job
            {
                JobId = JobList.Jobs.Count + 1,
                Status = Job.JobStatus.ToDo
            };

            TextRange script = new TextRange(ScriptTB.Document.ContentStart, ScriptTB.Document.ContentEnd);

            if (string.IsNullOrWhiteSpace(script.Text))
            {
                MessageBox.Show("Enter a Python script!");
                return;
            }
            if (!script.Text.Contains("def main():"))
            {
                MessageBox.Show("Cannot find the main method!");
                return;
            }

            byte[] textBytes = Encoding.UTF8.GetBytes(script.Text);
            job.PythonScript = Convert.ToBase64String(textBytes);

            SHA256 sha256hASH = SHA256.Create();
            byte[] hash = sha256hASH.ComputeHash(textBytes);
            job.Hash = hash;

            JobList.Jobs.Add(job);
        }

        private void UploadBtn_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog();
            openFileDialog.Filter = "Python Files (*.py)|*.py";

            if (openFileDialog.ShowDialog() == true)
            {
                string filePath = openFileDialog.FileName;
                string pythonCode = File.ReadAllText(filePath);

                Dispatcher.Invoke(() =>
                {
                    ScriptTB.Document.Blocks.Clear();
                    ScriptTB.AppendText(pythonCode);
                });
            }
        }

        // Function to update client status and number of jobs completed in the database
        private void UpdateClient()
        {
            try
            {
                // Get the current client information from the server
                RestRequest restRequest = new RestRequest("/api/clients/" + clientID, Method.Get);
                RestResponse restResponse = restClient.Execute(restRequest);

                if (restResponse.StatusCode != HttpStatusCode.OK)
                {
                    MessageBox.Show("Cannot connect to the Database!");
                    return;
                }

                Client client = JsonConvert.DeserializeObject<Client>(restResponse.Content);

                // Update the client's status and number of completed jobs
                client.JobsCompleted = totalJobsCompleted;
                client.Status = currJobProgress;

                restRequest = new RestRequest("/api/clients/" + clientID, Method.Put);
                restRequest.AddJsonBody(client);

                restResponse = restClient.Execute(restRequest);

                if (restResponse.StatusCode != HttpStatusCode.NoContent)
                {
                    MessageBox.Show("Cannot update the client!");
                    return;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void MainWindow_OnClosing(object sender, CancelEventArgs e)
        {
            RestRequest restRequest = new RestRequest("/api/clients/" + clientID, Method.Delete);
            RestResponse restResponse = restClient.Execute(restRequest);

            if (restResponse.StatusCode != HttpStatusCode.NoContent)
            {
                isClosed = true;
                // MessageBox.Show("Client can't be removed!");
            }
        }
    }
}
