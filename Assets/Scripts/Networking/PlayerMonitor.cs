using Photon.Pun;
using UnityEngine;
using UnityEngine.UI;

namespace CardsVR.Networking
{
    /*
     *  A utility function that monitors the player status and updates
     *  the associated text label with the information.
     */
    public class PlayerMonitor : MonoBehaviour
    {
        /*
        *      Update is run on every frame by Unity Engine.
        *      
        *      Parameters
        *      ----------
        *      None
        *      
        *      Returns
        *      -------
        *      None
        */
        void Update()
        {
            Text label = GetComponent<Text>();
            if (PhotonNetwork.InRoom && PlayerManager.Instance.player != null)
                label.text = PlayerManager.Instance.player.NickName;
            else
                label.text = "Connecting";
        }
    }
}
