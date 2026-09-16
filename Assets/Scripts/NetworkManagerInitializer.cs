using Unity.Netcode;
using UnityEngine;

public class NetworkManagerInitializer : MonoBehaviour
{
    private static NetworkManagerInitializer instance;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
    }
}
