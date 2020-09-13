
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
    public int queueCapacity = 30;
    public float beatsPerMinute = 132;
    public float songDuration = 15;
    public float noteSpeed = 10;
    public Vector3 noteStartingRotation = new Vector3(0, 90, 0);
    //public float spawnRangeX = 5;
    //public float spawnRangeY = 0;
    //public float spawnRangeZ = 5;
    //public float despawnDistance = 15;
    [TextArea(15, 20)] public string SongDataString = "";

    [Header("Note data")]
    private float[] _time = new float[0];
    private int[] _lineIndex = new int[0];
    private int[] _lineLayer = new int[0];
    private int[] _type = new int[0];
    private int[] _cutDirection = new int[0];

    private float songRemainingTimer;
    private bool isSongActive = false;
    private GameObject[] noteBlockPool = new GameObject[0];
    private int currentNotePoolIndex = 0;
    private int nextTimeIndex = 0;
    private int totalSongNotes;
    private float accumulatedBeats = 0;
    private int avaliableActivePoolIndex = 0;
    [SerializeField]
    private Transform[] activeNotesPool = new Transform[0];
    private float elapsedTime = 0f;

    // Start is called before the first frame update
    void Start()
    {
        //  Create pool of note blocks
        InitializeNoteBlockPool();

        ParseAndCacheSongData();

        StartSong();
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
        Debug.Log("Parsing song data...");

        //  Clean up string data
        SongDataString = SongDataString.Replace(" ", String.Empty);
        SongDataString = SongDataString.Replace("\"", String.Empty);
        SongDataString = SongDataString.Replace("\n", String.Empty);
        SongDataString = SongDataString.Replace("\r", String.Empty);
        SongDataString = SongDataString.Replace("\t", String.Empty);
        //Debug.Log(SongDataString);

        //  Parse song data
        string[] notes = SongDataString.Split(new[] { "},{" }, StringSplitOptions.None);
        totalSongNotes = notes.Length;

        //  cleanup leading and trailing characters
        notes[0] = notes[0].Replace("{", String.Empty);
        notes[notes.Length - 1] = notes[notes.Length - 1].Replace("}", String.Empty);

        //  init cache array
        //Debug.Log("NOTES LENGTH: " + notes.Length);
        _time = new float[notes.Length];
        _lineIndex = new int[notes.Length];
        _lineLayer = new int[notes.Length];
        _type = new int[notes.Length];
        _cutDirection = new int[notes.Length];

        for (int i = 0; i < notes.Length; i++)
        {
            //Debug.Log(notes[i]);
            //Debug.Log("=================");

            //  Parse note data
            string[] parameters = notes[i].Split(',');

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

        //Debug.Log("==============");
        //for (int i = 0; i < _time.Length; i++)
        //{
        //    Debug.Log(_time[i]);
        //}
        //Debug.Log("==============");

        //Debug.Log("Parse completed.");
    } 
    #endregion

    private void SpawnNoteOnBeat()
    {
        //  [OLD]
        //beatInterval -= Time.deltaTime;
        //if (beatInterval <= 0)
        //{
        //    beatInterval = 0.25f;

        //    //  spawn with random position
        //    noteBlockPool[currentNotePoolIndex].gameObject.SetActive(true);
        //    float randX = UnityEngine.Random.Range(-spawnRangeX, spawnRangeX);
        //    float randY = UnityEngine.Random.Range(-spawnRangeY, spawnRangeY);
        //    float randZ = UnityEngine.Random.Range(-spawnRangeZ, spawnRangeZ);
        //    Vector3 pos = transform.position + new Vector3(randX, randY, randZ);
        //    noteBlockPool[currentNotePoolIndex].transform.position = pos;

        //    //  enqueue
        //    bool isInserted = InsertIntoActivePool(noteBlockPool[currentNotePoolIndex].transform);
        //    if (isInserted == false)
        //    {
        //        Debug.LogError("pool is overloaded! Slow down spawnrate or increase pool capacity!");
        //    }

        //    //  cycle pool index
        //    currentNotePoolIndex++;
        //    if (currentNotePoolIndex == noteBlockPool.Length)
        //    {
        //        currentNotePoolIndex = 0;
        //    }
        //}

        //  [BEATS PER MINUTE METHOD]
        elapsedTime += Time.deltaTime;
        if (elapsedTime >= 1f)
        {
            elapsedTime = elapsedTime % 1f;

            accumulatedBeats += beatsPerMinute / 60.0f;
            //Debug.Log("ACCUMULATED BEATS: " + accumulatedBeats);

            //  Get next note to spawn if any
            float nextBeatTime = -1;
            do
            {
                //  if we still have notes left in the song...
                //Debug.Log("next beat index: " + nextTimeIndex);
                if (nextTimeIndex < _time.Length)
                {
                    //Debug.Log("next note's beat time ACTUAL: " + _time[nextTimeIndex]);
                    //Debug.Log("next note's beat time PRE: " + nextBeatTime);
                    nextBeatTime = _time[nextTimeIndex];
                    //Debug.Log("next note's beat time POST: " + nextBeatTime);

                    //  if the next beat time is less or equal to our acculated beats... spawn that note and increment index for our next note.
                    if (nextBeatTime <= accumulatedBeats)
                    {
                        //Debug.Log("Playing note at beat: " + nextBeatTime);

                        //  solve position for line column
                        Vector3 pos = transform.position;
                        if (_lineIndex[nextTimeIndex] == 0)
                            pos += new Vector3(0, 0, -1);
                        else if (_lineIndex[nextTimeIndex] == 1)
                            pos += new Vector3(0, 0, 0);
                        else if (_lineIndex[nextTimeIndex] == 2)
                            pos += new Vector3(0, 0, 1);
                        else if (_lineIndex[nextTimeIndex] == 3)
                            pos += new Vector3(0, 0, 2);

                        //  solve position for line layer
                        if (_lineLayer[nextTimeIndex] == 0)
                            pos += new Vector3(0, -1, 0);
                        else if (_lineLayer[nextTimeIndex] == 1)
                            pos += new Vector3(0, 0, 0);
                        else if (_lineLayer[nextTimeIndex] == 2)
                            pos += new Vector3(0, 1, 0);

                        //  solve for cut direction
                        Vector3 rot = Vector3.zero;
                        if (_cutDirection[nextTimeIndex] == 0)
                            rot = new Vector3(0, 0, 180);
                        else if (_cutDirection[nextTimeIndex] == 1)
                            rot = new Vector3(0, 0, 0);
                        else if (_cutDirection[nextTimeIndex] == 2)
                            rot = new Vector3(0, 0, 90);
                        else if (_cutDirection[nextTimeIndex] == 3)
                            rot = new Vector3(0, 0, 270);
                        else if (_cutDirection[nextTimeIndex] == 4)
                            rot = new Vector3(0, 0, 135);
                        else if (_cutDirection[nextTimeIndex] == 5)
                            rot = new Vector3(0, 0, 225);
                        else if (_cutDirection[nextTimeIndex] == 6)
                            rot = new Vector3(0, 0, 45);
                        else if (_cutDirection[nextTimeIndex] == 7)
                            rot = new Vector3(0, 0, 315);
                        //else if (_lineLayer[currentNotesIndex] == 8)
                        //    rot = new Vector3(0, 0, 0);

                        //  increment     
                        nextTimeIndex++;

                        //  spawn
                        noteBlockPool[currentNotePoolIndex].gameObject.SetActive(true);
                        noteBlockPool[currentNotePoolIndex].transform.position = pos;
                        noteBlockPool[currentNotePoolIndex].transform.rotation = Quaternion.Euler(rot + noteStartingRotation);

                        //  insert check
                        bool isInserted = InsertIntoActivePool(noteBlockPool[currentNotePoolIndex].transform);
                        if (isInserted == false)
                        {
                            Debug.LogError("pool is overloaded! Slow down spawnrate or increase pool capacity!");
                        }

                        //  cycle pool index
                        currentNotePoolIndex++;
                        if (currentNotePoolIndex >= noteBlockPool.Length)
                        {
                            currentNotePoolIndex = 0;
                        }
                    }
                    else
                    {
                        break;
                    }
                }
                else
                {
                    break;
                }
                
            } while (nextBeatTime >= 0);
        }
    }

    public bool InsertIntoActivePool(Transform tr)
    {
        if (activeNotesPool.Length <= 0)
            return false;

        if (avaliableActivePoolIndex >= 0)
        {
            activeNotesPool[avaliableActivePoolIndex] = tr;

            //  set new avaliable index
                //  micro otimization - check if the next position is avaliable, set it to that
            if (avaliableActivePoolIndex + 1 < activeNotesPool.Length && activeNotesPool[avaliableActivePoolIndex + 1] == null)
            {
                avaliableActivePoolIndex += 1;
            }
            else
            {
                //  Search for any new avaliable indexes
                avaliableActivePoolIndex = -1;
                for (int i = 0; i < activeNotesPool.Length; i++)
                {
                    if (activeNotesPool[i] == null)
                    {
                        avaliableActivePoolIndex = i;
                        break;
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

                //  update avaliable index
                avaliableActivePoolIndex = i;
                //if (avaliableActivePoolIndex < 0)
                //{
                //}

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

            //activeNotesPool[i].Translate(transform.forward * Time.deltaTime * speed);
            activeNotesPool[i].position += transform.forward * Time.deltaTime * noteSpeed;
        }
    }

    private void InitializeNoteBlockPool()
    {
        Debug.Log("Initializing note block pool...");

        noteBlockPool = new GameObject[poolAmount];

        for (int i = 0; i < poolAmount; i++)
        {
            noteBlockPool[i] = VRCInstantiate(noteBlockPrefab);
            noteBlockPool[i].transform.rotation = Quaternion.Euler(noteStartingRotation);

            //  Assign index id to noteblock using it's gameobject name
            noteBlockPool[i].name += "_" + i.ToString();
        }

        Debug.Log("Initializing note block pool COMPLETED.");
    }

    public void StartSong()
    {
        StopSong();

        Debug.Log("SONG STARTED.");

        //  Reset/re-initialize
        activeNotesPool = new Transform[queueCapacity];

        avaliableActivePoolIndex = 0;
        currentNotePoolIndex = 0;
        nextTimeIndex = 0;
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
