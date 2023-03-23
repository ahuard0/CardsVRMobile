using UnityEngine;
using OpenCVForUnity.CoreModule;
using OpenCVForUnity.ArucoModule;
using OpenCVForUnity.UnityUtils;
using System.Collections.Generic;
using CardsVR.Networking;
using CardsVR.Commands;
using Photon.Realtime;
using ExitGames.Client.Photon;
using Photon.Pun;

namespace CardsVR.Detection
{
    /*
     *      Operates the webcam and detects Aruco Tags in the Given Dictionaries.
     */
    public class FrameClient : MonoBehaviour, IOnEventCallback
    {
        [SerializeField]
        private SpriteRenderer _screen;
        [SerializeField]
        private int[] dictIDs;
        [SerializeField]
        private float arucoRate = 1.0f;
        [SerializeField]
        private int cameraWidth = 720;  // Width px
        [SerializeField]
        private int cameraHeight = 1280;  // Height px
        [SerializeField]
        private int cameraFPS = 60;

        private ArucoData _arucoData = new ArucoData();
        private WebCamDevice _cameraDevice;
        private WebCamTexture _cameraTexture;
        private float nextArucoTime;
        private Dictionary dict;
        private DetectorParameters arucoParam;
        private Texture2D tex;
        private Mat rgbMat;

        private List<int> uniqueIdList = new List<int>();
        private List<float> cornerList = new List<float>();
        private List<Mat> cornersMatList = new List<Mat>();
        private Mat tagIDs;

        /*
         *      Checks if the camera is ready.
         *      
         *      Accessor
         *      -------
         *      initialized : boolean
         *          A flag indicating whether the camera is playing.
         */
        private bool isReady
        {
            get
            {
                int rows = _cameraTexture.height;
                int cols = _cameraTexture.width;
                if (rows < 100 || cols < 100)  // a ready webcam has full resolution
                    return false;
                else
                    return true;
            }
        }
        
        /*
         *      Get the frame image from the webcam as an RGB OpenCV Mat object.
         *      
         *      Parameters
         *      -------
         *      None
         *      
         *      Returns
         *      -------
         *      None
         */
        private void GetFrameImage()
        {
            int rows = _cameraTexture.height;
            int cols = _cameraTexture.width;

            // Get RGB Image of the Frame
            if (_cameraTexture.deviceName.Contains("iVCam"))  // Connected to PC, use old rotation scheme
            {
                rgbMat = new Mat(rows: rows, cols: cols, type: CvType.CV_8UC3);
                Utils.webCamTextureToMat(webCamTexture: _cameraTexture, mat: rgbMat, flip: true, flipCode: 1);
            } 
            else  // Connected to iPhone Camera, Device Name: "Back Camera"
            {
                Mat temp = new Mat(rows: rows, cols: cols, type: CvType.CV_8UC3);
                Utils.webCamTextureToMat(webCamTexture: _cameraTexture, mat: temp, flip: true, flipCode: -1);  // iPhone Rear Camera correction: flip both vertically and horizontally (flip code: -1), then take the transpose.
                rgbMat = temp.t();
            }
        }

        /*
         *      Detect any Aruco Tags from the given libraries in the RGB image.
         *      
         *      Parameters
         *      -------
         *      None
         *      
         *      Returns
         *      -------
         *      None
         */
        private void DetectAruco()
        {
            // Search Each Aruco Dictionary for matches
            tagIDs = new Mat();
            foreach (int dictID in dictIDs)
            {
                dict = Aruco.getPredefinedDictionary(dictID);  // get dictionary definition by ID number

                List<Mat> corners = new List<Mat>();
                Aruco.detectMarkers(rgbMat, dict, corners, tagIDs, arucoParam);  // detect dictionary markers in the frame

                float[] cornersArray = new float[corners.Count * 8];  // Each tag has 4 corners with 2 coordinates each -> 8 numbers per tag
                if (corners.Count > 0)  // tags detected
                {
                    cornersMatList.AddRange(corners);  // log the tag corners (e.g., add a 28 Mat object array to the list)

                    for (int i = 0; i < corners.Count; i++)  // log the unique TagID, which depends on both the dictionary and the raw tag ID in a lookup table.
                    {
                        double[] item = tagIDs.get(i, 0);
                        int tagID = (int)item[0];  // raw TagID (e.g., 0-999)
                        int uniqueID = TagInfo.getUniqueIDFromArucoDictTagID(tagID, dictID);  // lookup unique ID from raw TagID and dictionary ID (e.g., 0-2999).
                        uniqueIdList.Add(uniqueID);
                    }

                    int j = 0;
                    foreach (Mat corner in corners)  // unwrap the corner Mat list into a list of floats
                    {
                        for (int i = 0; i < corner.cols(); i++)
                        {
                            double[] cornerArray = corner.get(0, i);  // get access function is necessary for extracting data in OpenCV Mat objects.
                            cornersArray[j * 8 + i * 2] = (float)cornerArray[0];  // X
                            cornersArray[j * 8 + i * 2 + 1] = (float)cornerArray[1];  // Y
                        }
                        j++;
                    }
                    cornerList.AddRange(cornersArray);  // List of floats is an easier format to use -> necessary for data transmittal over PUN

                }
            }
        }

        /*
         *      Cleans up the detected OpenCV formatted data and stores it in the simpler float 
         *      and integer arrays of an ArucoState object.
         *      
         *      Parameters
         *      -------
         *      None
         *      
         *      Returns
         *      -------
         *      None
         */
        private void UpdateArucoState()
        {
            int rows = _cameraTexture.height;
            int cols = _cameraTexture.width;

            if (uniqueIdList.Count > 0)  // Tags Detected
            {
                // Gather Data
                _arucoData.corners = cornerList.ToArray();
                _arucoData.uniqueIDs = uniqueIdList.ToArray();
                _arucoData.width = cols;
                _arucoData.height = rows;
            }
            else  // Tags Not Detected
            {
                _arucoData.Reset();  // Clear the Frame State Data
            }
        }

        /*
         *      Broadcast the detected Aruco Tags to the remote clients.
         *      
         *      Parameters
         *      -------
         *      None
         *      
         *      Returns
         *      -------
         *      None
         */
        private void BroadcastFrame()
        {
            int PlayerID = PlayerManager.Instance.PlayerNum;
            if (PlayerID == 0)  // skip frame if playerID not yet set
                return;

            if (_arucoData.Count > 0)
            {
                string PlayerNick = PhotonNetwork.NickName;
                FrameData frame = new FrameData(_arucoData, PlayerID, PlayerNick);
                SendData command = new SendData(data: frame, SendReliable: false, ReceiveLocally: true);
                Invoker.Instance.SetCommand(command);
                Invoker.Instance.ExecuteCommand(record: false);
            }
        }


        /*
         *      PUN uses this callback method to respond to PUN events. The client must first be 
         *      registered to receive PUN events.
         *      
         *      The client receives events and data from players on PUN, including ourself, using
         *      the OnEvent callback.  For example, invoking the a command will trigger
         *      the OnEvent callback for all players regardless of who sent the data in the first place.
         *      
         *      Parameters
         *      ----------
         *      photonEvent : EventData
         *          Contains a byte event code and an object array containing arbitrary data.
         *      
         *      Returns
         *      -------
         *      None
         */
        public void OnEvent(EventData photonEvent)
        {
            if (photonEvent.Code == FrameData.EventID)
            {
                object[] data = (object[])photonEvent.CustomData;
                FrameData msg = FrameData.FromObjectArray(data);
                Command command = new SyncFrame(msg);

                Invoker.Instance.SetCommand(command);
                Invoker.Instance.ExecuteCommand(record: false);
            }
        }

        /*
         *      Annotates the screen with the detected Aruco Tags superimposed on the RGB image.
         *      
         *      Parameters
         *      -------
         *      None
         *      
         *      Returns
         *      -------
         *      None
         */
        private void UpdateScreen()
        {
            if (uniqueIdList.Count > 0)  // Tags Detected
            {
                // Annotate Detected Tags
                Mat idsMat = new Mat(uniqueIdList.Count, 1, CvType.CV_32SC1);
                for (int i = 0; i < uniqueIdList.Count; i++)
                {
                    idsMat.put(i, 0, _arucoData.uniqueIDs[i]);
                }
                Aruco.drawDetectedMarkers(rgbMat, cornersMatList, idsMat);
            }

            // Clear Temporary Lists for Next Frame
            cornerList.Clear();
            uniqueIdList.Clear();
            cornersMatList.Clear();
            tagIDs.Dispose();

            // Draw Tag Annotations on Screen
            Destroy(tex);  // Clear Texture Memory
            tex = new Texture2D(rgbMat.cols(), rgbMat.rows(), TextureFormat.RGBA32, false, true);
            Utils.matToTexture2D(rgbMat, tex, true, 1);  // Generate texture

            UnityEngine.Rect newRect = new UnityEngine.Rect(0f, 0f, tex.width, tex.height);
            Vector2 pivot = new Vector2(0.5f, 0.5f);
            _screen.sprite = Sprite.Create(tex, newRect, pivot, 1f);  // Update screen

            rgbMat.Dispose();
        }

        /*
         *      A Unity MonoBehavior method that is called before all else.
         *      
         *      Sets the application parameters and specifies to use the
         *      default OpenCV detector parameters.
         *      
         *      Parameters
         *      -------
         *      None
         *      
         *      Returns
         *      -------
         *      None
         */
        private void Awake()
        {
            Application.targetFrameRate = 60;
            Application.RequestUserAuthorization(UserAuthorization.WebCam);

            Screen.orientation = ScreenOrientation.Portrait;

            arucoParam = DetectorParameters.create();
        }

        /*
         *      Unity MonoBehavior callback OnEnable is called whenever the attached 
         *      GameObject is enabled.  On scene load, this occurs after Awake and 
         *      Start MonoBehavior callbacks.
         *      
         *      This method registers the MessageClient to receive PUN events.
         *      
         *      Parameters
         *      ----------
         *      None
         *      
         *      Returns
         *      -------
         *      None
         */
        private void OnEnable()
        {
            PhotonNetwork.AddCallbackTarget(this);
        }

        /*
         *      Unity MonoBehavior callback OnDisable is called whenever the attached 
         *      GameObject is disabled.  This occurs when quitting the program or
         *      loading another scene.
         *      
         *      This method unregisters the MessageClient from receiving PUN events.
         *      
         *      Parameters
         *      ----------
         *      None
         *      
         *      Returns
         *      -------
         *      None
         */
        private void OnDisable()
        {
            PhotonNetwork.RemoveCallbackTarget(this);
        }

        /*
         *      A Unity MonoBehavior method that is called next after Awake().
         *      
         *      Sets up the camera and begins playback.
         *      
         *      Parameters
         *      -------
         *      None
         *      
         *      Returns
         *      -------
         *      None
         */
        void Start()
        {
            WebCamDevice[] devices = WebCamTexture.devices;
            _cameraDevice = devices[0]; // default
            for (int i = 0; i < devices.Length; i++)
            {
                if (devices[i].name.Contains("iVCam") || devices[i].name.Contains("FaceTime"))
                {
                    _cameraDevice = devices[i];
                    break;
                }
            }
            cameraWidth = Screen.width;
            cameraHeight = Screen.height;

            Debug.Log("Camera: " + _cameraDevice.name);

            _cameraTexture = new WebCamTexture(_cameraDevice.name, cameraWidth, cameraHeight, cameraFPS);
            _screen.material.mainTexture = _cameraTexture;
            _cameraTexture.Play();
        }

        /*
         *      A Unity MonoBehavior method that is called every frame.
         *      
         *      Parameters
         *      -------
         *      None
         *      
         *      Returns
         *      -------
         *      None
         */
        void Update()
        {
            if (_cameraTexture != null && _cameraTexture.isPlaying)
            {
                if (Time.time > nextArucoTime)
                {
                    nextArucoTime = arucoRate + Time.time;

                    if (!isReady)
                        return;  // camera not yet initialized -> skip frames and wait for it

                    GetFrameImage();
                    DetectAruco();
                    UpdateArucoState();
                    BroadcastFrame();
                    UpdateScreen();
                }
            }
        }

    }
}
