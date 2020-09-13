
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UI;

public class NoteBlockSpawner : UdonSharpBehaviour
{
    [Header("Settings")]
    public GameObject noteBlockPrefab;
    public int poolAmount = 10;
    public string SongDataString = "";
    public float songDuration = 15;
    public float speed = 10;
    public float spawnRangeX = 5;
    public float spawnRangeY = 0;
    public float spawnRangeZ = 5;
    public float despawnDistance = 15;
    public int queueCapacity = 30;

    [Header("Note data")]
    private float[] _time;
    private int[] _lineIndex;
    private int[] _lineLayer;
    private int[] _type;
    private int[] _cutDirection;

    private float songRemainingTimer;
    private bool isSongActive = false;
    private GameObject[] noteBlockPool = new GameObject[0];
    private int currentNotePoolIndex = 0;
    private float beatInterval = 0;
    private int totalSongNotes;
    private float beatsPerMinute;
    private float accumulatedBeats = 0;
    private int avaliableActivePoolIndex = 0;
    private Transform[] activeNotesPool = new Transform[0];

    // Start is called before the first frame update
    void Start()
    {
        //  Create pool of note blocks
        InitializeNoteBlockPool();

        ParseAndCacheSongData();
    }

    public void Update()
    {
        if (isSongActive == false)
            return;
        if (noteBlockPool.Length <= 0)
            return;

        //  song timer
        UpdateSongTimer();

        //  spawn noteblocks
        SpawnNoteOnBeat();

        //  Translate active blocks
        TranslateNoteBlocks();

        //  Update ui [BUG] enabling this break the whole thing, idk why
        //arrayUI.text = "";
        //for (int i = 0; i < activeNoteBlocksQueue.Length; i++)
        //{
        //    if (!activeNoteBlocksQueue[i].gameObject.activeInHierarchy)
        //        continue;

        //    arrayUI.text += activeNoteBlocksQueue[i].name + "\n";
        //}
    }

    #region ParseAndCacheSongData()
    private void ParseAndCacheSongData()
    {
        //  Parse song data
        SongDataString = SongDataString.Replace(" ", String.Empty);
        SongDataString = SongDataString.Replace("\"", String.Empty);
        //Debug.Log(SongDataString);
        string[] notes = SongDataString.Split(new[] { "},{" }, StringSplitOptions.None);
        totalSongNotes = notes.Length;

        //  cleanup leading and trailing characters
        notes[0] = notes[0].Replace("{", String.Empty);
        notes[notes.Length - 1] = notes[notes.Length - 1].Replace("}", String.Empty);

        for (int i = 0; i < notes.Length; i++)
        {
            //Debug.Log(notes[i]);
            //Debug.Log("=================");

            //  Parse note data
            string[] parameters = notes[i].Split(',');

            //  init cache array
            _time = new float[notes.Length];
            _lineIndex = new int[notes.Length];
            _lineLayer = new int[notes.Length];
            _type = new int[notes.Length];
            _cutDirection = new int[notes.Length];

            for (int j = 0; j < parameters.Length; j++)
            {
                //  parse
                //Debug.Log(noteDatas[j]);
                string[] param = parameters[j].Split(':');

                //Debug.Log(string.Join(" , ", param));

                if (param[0].Contains("_time"))
                    _time[i] = float.Parse(param[1]);
                else if (param[0].Contains("_lineIndex"))
                    _lineIndex[i] = int.Parse(param[1]);
                else if (param[0].Contains("_lineLayer"))
                    _lineLayer[i] = int.Parse(param[1]);
                else if (param[0].Contains("_type"))
                    _type[i] = int.Parse(param[1]);
                else if (param[0].Contains("_cutDirection"))
                    _cutDirection[i] = int.Parse(param[1]);
            }

            //Debug.Log(_time[i]);
            //Debug.Log(_lineIndex[i]);
            //Debug.Log(_lineLayer[i]);
            //Debug.Log(_type[i]);
            //Debug.Log(_cutDirection[i]);
        }
    } 
    #endregion

    private void SpawnNoteOnBeat()
    {
        //  [OLD]
        beatInterval -= Time.deltaTime;
        if (beatInterval <= 0)
        {
            beatInterval = 0.25f;

            //  spawn with random position
            noteBlockPool[currentNotePoolIndex].gameObject.SetActive(true);
            float randX = UnityEngine.Random.Range(-spawnRangeX, spawnRangeX);
            float randY = UnityEngine.Random.Range(-spawnRangeY, spawnRangeY);
            float randZ = UnityEngine.Random.Range(-spawnRangeZ, spawnRangeZ);
            Vector3 pos = transform.position + new Vector3(randX, randY, randZ);
            noteBlockPool[currentNotePoolIndex].transform.position = pos;

            //  enqueue
            bool isInserted = InsertIntoActivePool(noteBlockPool[currentNotePoolIndex].transform);
            if (isInserted == false)
            {
                Debug.LogError("pool is overloaded! Slow down spawnrate or increase pool capacity!");
            }

            //  cycle pool index
            currentNotePoolIndex++;
            if (currentNotePoolIndex == noteBlockPool.Length)
            {
                currentNotePoolIndex = 0;
            }
        }

        //  [BEATS PER MINUTE METHOD]
        //var nextNoteBlockTime = 0;
        //beatInterval -= Time.deltaTime;
        //if (beatInterval <= 0)
        //{
        //    beatInterval = 60 / beatsPerMinute;
        //    accumulatedBeats += beatInterval;

        //    if (accumulatedBeats <= nextNoteBlockTime)
        //    {
        //        //  spawn with random position
        //        noteBlockPool[currentActiveSpawnIndex].gameObject.SetActive(true);
        //        float randX = UnityEngine.Random.Range(-spawnRangeX, spawnRangeX);
        //        float randY = UnityEngine.Random.Range(-spawnRangeY, spawnRangeY);
        //        float randZ = UnityEngine.Random.Range(-spawnRangeZ, spawnRangeZ);
        //        Vector3 pos = transform.position + new Vector3(randX, randY, randZ);
        //        noteBlockPool[currentActiveSpawnIndex].transform.position = pos;

        //        //  enqueue
        //        QueueEnqueue(noteBlockPool[currentActiveSpawnIndex].transform);

        //        currentActiveSpawnIndex++;
        //    }
        //}
    }

    public bool InsertIntoActivePool(Transform tr)
    {
        if (activeNotesPool.Length <= 0)
            return false;

        if (avaliableActivePoolIndex >= 0)
        {
            activeNotesPool[avaliableActivePoolIndex] = tr;

            //  set new avaliable index
            //  micro otimization using prediction
            if (avaliableActivePoolIndex + 1 < activeNotesPool.Length && activeNotesPool[avaliableActivePoolIndex + 1] == null)
            {
                avaliableActivePoolIndex += 1;
            }
            else
            {
                avaliableActivePoolIndex = -1;
                for (int i = 0; i < activeNotesPool.Length; i++)
                {
                    if (activeNotesPool[i] == null)
                    {
                        avaliableActivePoolIndex = i;
                    }
                }
            }

            return true;
        }
        else
        {
            return false;
        }
    }

    public void RemoveFromActivePool(Transform tr)
    {
        if (activeNotesPool.Length <= 0)
            return;

        for (int i = 0; i < activeNotesPool.Length; i++)
        {
            if (activeNotesPool[i] == tr)
            {
                //  despawn
                activeNotesPool[i].gameObject.SetActive(false);

                //  remove
                activeNotesPool[i] = null;

                //  update index
                if (avaliableActivePoolIndex < 0)
                {
                    avaliableActivePoolIndex = i;
                }

                break;
            }
        }

        return;
    }

    private void UpdateSongTimer()
    {
        songRemainingTimer -= Time.deltaTime;
        if (songRemainingTimer <= 0)
        {
            StopSong();
        }
    }

    private void TranslateNoteBlocks()
    {
        for (int i = 0; i < activeNotesPool.Length; i++)
        {
            if (activeNotesPool[i] == null || activeNotesPool[i].gameObject.activeInHierarchy == false)
                continue;

            activeNotesPool[i].Translate(transform.forward * Time.deltaTime * speed);
        }
    }

    private void InitializeNoteBlockPool()
    {
        Debug.Log("Initializing note block pool...");

        noteBlockPool = new GameObject[poolAmount];

        for (int i = 0; i < poolAmount; i++)
        {
            noteBlockPool[i] = VRCInstantiate(noteBlockPrefab);

            //  Assign index id to noteblock using it's gameobject name
            noteBlockPool[i].name += "_" + i.ToString();
        }
    }

    public void StartSong()
    {
        Debug.Log("SONG STARTED.");

        StopSong();

        //  Reset/re-initialize
        activeNotesPool = new Transform[queueCapacity];

        avaliableActivePoolIndex = 0;
        currentNotePoolIndex = 0;
        beatInterval = 0;
        accumulatedBeats = 0;

        songRemainingTimer = songDuration;
        isSongActive = true;
    }

    public void StopSong()
    {
        Debug.Log("SONG STOPPED.");

        isSongActive = false;

        DestroyActiveBlocks();
    }

    private void DestroyActiveBlocks()
    {
        if (activeNotesPool.Length <= 0)
            return;

        Debug.Log("DESTROYING ACTIVE BLOCKS...");

        for (int i = 0; i < activeNotesPool.Length; i++)
        {
            if (activeNotesPool[i] == null)
                continue;

            activeNotesPool[i].gameObject.SetActive(false);
            activeNotesPool[i] = null;
        }

        activeNotesPool = new Transform[0];
        avaliableActivePoolIndex = -1;
    }
}
