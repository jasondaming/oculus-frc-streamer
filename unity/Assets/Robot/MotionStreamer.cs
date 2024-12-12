/* Unity-based example for publishing Quest headset data to FRC-compatible Network Tables */
/* Juan Chong - 2024 */
using UnityEngine;
using NetworkTables;
using System.Net;
using System.Linq;
using System.Net.Sockets;
using TMPro;
using UnityEngine.UI;
using System;
using UnityEngine.EventSystems;

/* Extend Vector3 with a ToArray() function */
public static class VectorExtensions
{
    public static float[] ToArray(this Vector3 vector)
    {
        return new float[] { vector.x, vector.y, vector.z };
    }
}

/* Extend Quaternion with a ToArray() function */
public static class QuaternionExtensions
{
    public static float[] ToArray(this Quaternion quaternion)
    {
        return new float[] { quaternion.x, quaternion.y, quaternion.z, quaternion.w };
    }
}

public class MotionStreamer : MonoBehaviour
{
    /* Initialize local variables */
    public int frameIndex; // Local variable to store the headset frame index
    public double timeStamp; // Local variable to store the headset timestamp
    public Vector3 position; // Local variable to store the headset position in 3D space
    public Quaternion rotation; // Local variable to store the headset rotation in quaternion form
    public Vector3 eulerAngles; // Local variable to store the headset rotation in Euler angles
    public OVRCameraRig cameraRig;
    public Nt4Source frcDataSink = null;
    private long command = 0;

    public TMP_InputField teamInput;
    public Button teamUpdateButton;
    private TouchScreenKeyboard overlayKeyboard;
    public static string inputText = "9999";
    private string teamNumber = "9999";


    [SerializeField] public Transform vrCamera; // The VR camera transform
    [SerializeField] public Transform vrCameraRoot; // The root of the camera transform
    [SerializeField] public Transform resetTransform; // The desired position & rotation (look direction) for your player

    /* NT configuration settings */
    private readonly string appName = "Quest3S"; // A fun name to ID the client in the robot logs
    private readonly string serverAddress = "10.TE.AM.2"; // RoboRIO IP Address
    private readonly string serverDNS = "roboRIO-####-FRC.local"; // RoboRIO DNS Address
    private string ipAddress = "";
    private bool useAddress = true;
    private readonly int serverPort = 5810; // Typically 5810
    private int delayCounter = 0; // Counter used to delay checking for commands from the robot

    void Start()
    {
        teamNumber = PlayerPrefs.GetString("TeamNumber", "6391");
        setInputBox(teamNumber);
        teamInput.Select();
        ConnectToRobot();
        // Register the click listener on the button
        teamUpdateButton.onClick.AddListener(UpdateTeamNumber);
        // Subscribe to the TMP InputField's OnSelect event
        teamInput.onSelect.AddListener(OnInputFieldSelected);
    }

    void LateUpdate()
    {
        if (frcDataSink.Client.Connected())
        {
            PublishFrameData();

            if (delayCounter >= 0)
            {
                ProcessCommands();
                delayCounter = 0;
            }
            else
            {
                delayCounter++;
            }
        }
        else
        {
            HandleDisconnectedState();
        }
    }

    // Connect to the RoboRIO and publish topics
    private void ConnectToRobot()
    {
        if (useAddress == true)
        {
            ipAddress = getIP();
            useAddress = false;
        } else
        {
            try
            {
                IPHostEntry hostEntry = Dns.GetHostEntry(getDNS());
                //Filter to just IPv4 addresses
                IPAddress[] ipv4Addresses = hostEntry.AddressList
                   .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork)
                   .ToArray();
                ipAddress = ipv4Addresses[0].ToString();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log($"Error resolving DNS name: {ex.Message}");
            }
            
            useAddress = true;
        }

        UnityEngine.Debug.Log("[MotionStreamer] Attempting to connect to the RoboRIO at " + ipAddress + ".");
        frcDataSink = new Nt4Source(appName, ipAddress, serverPort);
        PublishTopics();
    }

    // Handle the disconnected state (RIO reboot, code restart, etc.)
    private void HandleDisconnectedState()
    {
        UnityEngine.Debug.Log("[MotionStreamer] Robot disconnected. Resetting connection and attempting to reconnect...");
        frcDataSink.Client.Disconnect();
        ConnectToRobot();
    }

    // Publish topics to Network Tables
    private void PublishTopics()
    {
        frcDataSink.PublishTopic("/oculus/miso", "int");
        frcDataSink.PublishTopic("/oculus/frameCount", "int");
        frcDataSink.PublishTopic("/oculus/timestamp", "double");
        frcDataSink.PublishTopic("/oculus/position", "float[]");
        frcDataSink.PublishTopic("/oculus/quaternion", "float[]");
        frcDataSink.PublishTopic("/oculus/eulerAngles", "float[]");
        frcDataSink.Subscribe("/oculus/mosi", 0.1, false, false, false);
    }

    // Publish the Quest pose data to Network Tables
    private void PublishFrameData()
    {
        frameIndex = UnityEngine.Time.frameCount;
        timeStamp = UnityEngine.Time.time;
        position = cameraRig.centerEyeAnchor.position;
        rotation = cameraRig.centerEyeAnchor.rotation;
        eulerAngles = cameraRig.centerEyeAnchor.eulerAngles;

        frcDataSink.PublishValue("/oculus/frameCount", frameIndex);
        frcDataSink.PublishValue("/oculus/timestamp", timeStamp);
        frcDataSink.PublishValue("/oculus/position", position.ToArray());
        frcDataSink.PublishValue("/oculus/quaternion", rotation.ToArray());
        frcDataSink.PublishValue("/oculus/eulerAngles", eulerAngles.ToArray());
    }

    // Process commands from the robot
    private void ProcessCommands()
    {
        command = frcDataSink.GetLong("/oculus/mosi");
        switch (command)
        {
            case 1:
                RecenterPlayer();
                UnityEngine.Debug.Log("[MotionStreamer] Processed a heading reset request.");
                frcDataSink.PublishValue("/oculus/miso", 99);
                break;
            default:
                frcDataSink.PublishValue("/oculus/miso", 0);
                break;
        }
    }

    // Clean up if the app crashes or is stopped
    void OnApplicationQuit()
    {

    }

    // Transform the HMD's rotation to virtually "zero" the robot position. Similar result as long-pressing the Oculus button.
    void RecenterPlayer()
    {
        float rotationAngleY = vrCamera.rotation.eulerAngles.y - resetTransform.rotation.eulerAngles.y;

        vrCameraRoot.transform.Rotate(0, -rotationAngleY, 0);

        Vector3 distanceDiff = resetTransform.position - vrCamera.position;
        vrCameraRoot.transform.position += distanceDiff;

    }

    public void UpdateTeamNumber()
    {
        UnityEngine.Debug.Log("[MotionStreamer] Updating Team Number");
        // Retrieve the text from the input field
        teamNumber = teamInput.text;

        // Save to persistant storage
        PlayerPrefs.SetString("TeamNumber", teamNumber);
        PlayerPrefs.Save(); // Ensure the data is written to disk

        setInputBox(teamNumber);
    }

    private void setInputBox(string team)
    {
        // Clear the input field
        teamInput.text = string.Empty;

        // Try to get the placeholder as a TextMeshProUGUI
        TextMeshProUGUI placeholderText = teamInput.placeholder as TextMeshProUGUI;
        if (placeholderText != null)
        {
            // Directly set the placeholder text
            placeholderText.text = "Next value (previous: " + team + ")";
        }
        else
        {
            // If the placeholder is not a TextMeshProUGUI, ensure the Inspector setup is correct
            Debug.LogError("Placeholder is not assigned or not a TextMeshProUGUI component.");
        }
    }

    private string getDNS()
    {
        return serverDNS.Replace("####", teamNumber);
    }

    private string getIP()
    {
        // Determine the TE and AM parts
        string tePart = teamNumber.Length > 2 ? teamNumber.Substring(0, teamNumber.Length - 2) : "0";
        string amPart = teamNumber.Length > 2 ? teamNumber.Substring(teamNumber.Length - 2) : teamNumber;

        return serverAddress.Replace("TE", tePart).Replace("AM", amPart);
    }

    private void OnInputFieldSelected(string text)
    {
        UnityEngine.Debug.Log("[MotionStreamer] Input Selected");
        // Show the Oculus keyboard
        overlayKeyboard = TouchScreenKeyboard.Open("", TouchScreenKeyboardType.NumberPad);
    }
}