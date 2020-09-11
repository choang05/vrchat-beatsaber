
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class NoteBlock : UdonSharpBehaviour
{
    public string saberTag;

    private void OnTriggerEnter(Collider other)
    {
        if (other.name.Contains(saberTag))
        {
            Debug.Log("HIT: " + other.gameObject.name);
            gameObject.SetActive(false);
        }
    }
}
