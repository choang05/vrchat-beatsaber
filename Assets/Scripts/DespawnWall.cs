
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class DespawnWall : UdonSharpBehaviour
{
    public string noteBlockTag;

    private NoteBlockSpawner noteSpawner;

    private void Start()
    {
        noteSpawner = GetComponentInParent<NoteBlockSpawner>();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.name.Contains(noteBlockTag))
        {
            noteSpawner.RemoveFromActivePool(other.transform);
        }
    }
}
