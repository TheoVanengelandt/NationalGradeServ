using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NationalServ  
{

	// State object for reading client data asynchronously  
	public class StateObject
	{
		// Client  socket.  
		public Socket workSocket = null;
		// Size of receive buffer.  
		public const int BufferSize = 1024;
		// Receive buffer.  
		public byte[] buffer = new byte[BufferSize];
		// Received data string.  
		public StringBuilder sb = new StringBuilder();
	}

	// class type for student grades
	public class NotesEleve
	{
		public string nomEleve;

		public List<MoyenneMatiere> listMoyenneMatiere;
	}

	public class MoyenneMatiere
	{
		public string matiere;
		public float moyenne;
		public List<float> notes;
	}

	public class AsynchronousSocketListener
	{
		// Thread signal.  
		public static ManualResetEvent allDone = new ManualResetEvent(false);

		// Create a Mutex
		private static Mutex mut = new Mutex();

		// Create a second Mutex
		private static Mutex mut2 = new Mutex();

		// List of grades received
		public static List<string> NotesList { get; set; } = new List<string>();

		// Full list of grades and average
		public static List<NotesEleve> MoyennesElevesMatieres { get; set; } = new List<NotesEleve>();

		public AsynchronousSocketListener()
		{
		}

		public static void StartListening()
		{
			// Establish the local endpoint for the socket.
			// The DNS name of the computer
			// running the listener is "host.contoso.com".
			IPHostEntry ipHostInfo = Dns.GetHostEntry(Dns.GetHostName());
			IPAddress ipAddress = ipHostInfo.AddressList[0];
			IPEndPoint localEndPoint = new IPEndPoint(ipAddress, 11000);

			// Create a TCP/IP socket.  
			Socket listener = new Socket(ipAddress.AddressFamily,
				SocketType.Stream, ProtocolType.Tcp);

			// Bind the socket to the local endpoint and listen for incoming connections.  
			try
			{
				listener.Bind(localEndPoint);
				listener.Listen(100);

				while (true)
				{
					// Set the event to nonsignaled state.  
					allDone.Reset();

					// Start an asynchronous socket to listen for connections.  
					Console.WriteLine("\nWaiting for a connection...");
					Console.WriteLine("Connected to " + Dns.GetHostName());
					listener.BeginAccept(
						new AsyncCallback(AcceptCallback),
						listener);

					// Wait until a connection is made before continuing.  
					allDone.WaitOne();
				}

			}
			catch (Exception e)
			{
				Console.WriteLine(e.ToString());
			}

			Console.WriteLine("\nPress ENTER to continue...");
			Console.Read();

		}

		public static void AcceptCallback(IAsyncResult ar)
		{
			// Signal the main thread to continue.  
			allDone.Set();

			// Get the socket that handles the client request.  
			Socket listener = (Socket)ar.AsyncState;
			Socket handler = listener.EndAccept(ar);

			// Create the state object.  
			StateObject state = new StateObject
			{
				workSocket = handler
			};
			handler.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
				new AsyncCallback(ReadCallback), state);
		}

		public static void ReadCallback(IAsyncResult ar)
		{
			String content = String.Empty;

			// Retrieve the state object and the handler socket  
			// from the asynchronous state object.  
			StateObject state = (StateObject)ar.AsyncState;
			Socket handler = state.workSocket;

			// Read data from the client socket.   
			int bytesRead = handler.EndReceive(ar);

			if (bytesRead > 0)
			{
				// There  might be more data, so store the data received so far.  
				state.sb.Append(Encoding.ASCII.GetString(
					state.buffer, 0, bytesRead));

				// Check for end-of-file tag. If it is not there, read   
				// more data.  
				content = state.sb.ToString();
				if (content.IndexOf("<EOF>") > -1)
				{
					// All the data has been read from the   
					// client. Display it on the console.  
					Console.WriteLine("\nRead {0} bytes from socket.",
						content.Length);

					// Echo the data back to the client.  
					Send(handler, content);

					// Wait until it is safe to enter.
					mut.WaitOne();

					// Add content receive to the gradesList and remove <EOL>
					NotesList.AddRange(content.Split('\n')
						.Where(i => i != content.Split('\n')
						.Last())
						.ToList());

					// Console.WriteLine("\n NEW GRADES ADDED");
					// Thread.Sleep(1000);

					// Release the Mutex.
					mut.ReleaseMutex();

					// Create a thread that add new grades to the average
					Task.Factory.StartNew(HandleNewGrades);
				}
				else
				{
					// Not all data received. Get more.  
					handler.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
					new AsyncCallback(ReadCallback), state);
				}
			}
		}

		private static void Send(Socket handler, String data)
		{
			// Convert the string data to byte data using ASCII encoding.  
			byte[] byteData = Encoding.ASCII.GetBytes(data);

			// Begin sending the data to the remote device.  
			handler.BeginSend(byteData, 0, byteData.Length, 0,
				new AsyncCallback(SendCallback), handler);
		}

		private static void SendCallback(IAsyncResult ar)
		{
			try
			{
				// Retrieve the socket from the state object.  
				Socket handler = (Socket)ar.AsyncState;

				// Complete sending the data to the remote device.  
				int bytesSent = handler.EndSend(ar);
				Console.WriteLine("Sent {0} bytes to client.\n", bytesSent);

				handler.Shutdown(SocketShutdown.Both);
				handler.Close();

			}
			catch (Exception e)
			{
				Console.WriteLine(e.ToString());
			}
		}

		public static void HandleNewGrades()
		{
			try
			{
				// Wait until it is safe to enter.
				mut2.WaitOne();

				while (NotesList.Count() != 0)
				{
					// Add new grades to the average as a string 
					AddNewGrades(NotesList.Last());

					// Remove the grades add from waiting list
					NotesList = NotesList.FindAll(notes => notes != NotesList.Last());
				}

				// Print the 'MoyennesElevesMatieres' list items
				foreach (var eleve in MoyennesElevesMatieres)
				{
					Console.WriteLine("\nL'eleve {0}à :", eleve.nomEleve);

					foreach (var matiere in eleve.listMoyenneMatiere)
					{
						Console.Write("{0:N2} de moyenne en{1} avec les notes: ", matiere.moyenne, matiere.matiere);
						for (int i = 0; i < matiere.notes.Count; i++)
						{
							float note = matiere.notes[i];
							Console.Write("{0} ", note);
						}
						Console.WriteLine();

					}
				}

				// Console.WriteLine("\nAVERAGE CALCULATED");
				// Thread.Sleep(1000);

				// Release the Mutex.
				mut2.ReleaseMutex();
			}
			catch (Exception e)
			{
				Console.WriteLine(e.ToString());
			}
		}

		private static void AddNewGrades(string v)
		{
			List<string> s = v.Split(';').ToList();

			float moy = 0;
			List<float> not = new List<float>();

			foreach (string note in s[2].Split(',').ToList())
			{
				// Parse the note (a string) into a float
				float intNote = float.Parse(note, CultureInfo.InvariantCulture.NumberFormat);

				not.Add(intNote);
				moy += intNote;
			}

			// Divide the sum of the notes by the number of notes to have the average
			moy /= not.Count();

			// Create an object with stuudent name, and a single item list with matter and average
			NotesEleve newMoyenne = new NotesEleve()
			{
				nomEleve = s[0],
				listMoyenneMatiere = new List<MoyenneMatiere>()
				{
					new MoyenneMatiere()
					{
						matiere = s[1],
						notes = not,
						moyenne = moy,
					}
				}
			};

			// Verify if the 'MoyennesElevesMatieres' list exit and if the student is in it
			if (MoyennesElevesMatieres.Count() != 0 && MoyennesElevesMatieres.Exists(eleve => eleve.nomEleve == newMoyenne.nomEleve) == true)
			{
				NotesEleve eleve = MoyennesElevesMatieres.Find(e => e.nomEleve == newMoyenne.nomEleve);

				// Verify if the student already has an average and grades for the matter we want to add
				if (eleve.listMoyenneMatiere.Exists(mat => mat.matiere == newMoyenne.listMoyenneMatiere.First().matiere) == true)
				{
					// get the new matter, average and grades
					MoyenneMatiere newMatiereNotes = newMoyenne.listMoyenneMatiere.First();

					// get the old matter, average and grades
					MoyenneMatiere oldMatiereNotes = eleve.listMoyenneMatiere
						.Find(mat => mat.matiere == newMatiereNotes.matiere);

					float sommeMoyenne = 0;
					List<float> sommeNotes = new List<float>();

					if(oldMatiereNotes.notes.Count() != 0 || newMatiereNotes.notes.Count() != 0)
					{
						sommeNotes.AddRange(oldMatiereNotes.notes);
						sommeNotes.AddRange(newMatiereNotes.notes);
					}

					// Calc the sum of the notes and divide it by the number of notes to have the average
					foreach (float grade in sommeNotes)
					{
						sommeMoyenne += grade;
					}
					sommeMoyenne = sommeMoyenne / sommeNotes.Count();

					// Get student's matter and add new average and grades
					eleve.listMoyenneMatiere.Find(user => user.matiere == newMoyenne.listMoyenneMatiere.First().matiere).moyenne = sommeMoyenne;
					eleve.listMoyenneMatiere.Find(user => user.matiere == newMoyenne.listMoyenneMatiere.First().matiere).notes = sommeNotes;

				} else
				{
					// Add the new matter's average in the list
					eleve.listMoyenneMatiere.Add(newMoyenne.listMoyenneMatiere.First());
				}

			} else
			{
				MoyennesElevesMatieres.Add(newMoyenne);
			}
		}

		public static void Main(String[] args)
		{
			Thread t = new Thread(new ThreadStart(StartListening));
			t.Start();
		}
	}
}

