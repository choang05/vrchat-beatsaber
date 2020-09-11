
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.UI;

public class NoteBlockSpawner : UdonSharpBehaviour
{
    [Header("Settings")]
    public GameObject noteBlockPrefab;
    public int poolAmount = 10;
    public float songDuration = 10;
    public int songNoteAmount = 5;
    public float speed = 10;
    public float spawnRangeX = 5;
    public float spawnRangeY = 0;
    public float spawnRangeZ = 5;
    public float despawnDistance = 15;
    public int queueCapacity = 30;
    public Text arrayUI;

    [Header("Note data")]
    private float[] _time;
    private int[] _lineIndex;
    private int[] _lineLayer;
    private int[] _type;
    private int[] _cutDirection;

    private float songTimer;
    private bool isSongActive = false;
    private GameObject[] noteBlockPool;
    private int currentActiveSpawnIndex = 0;
    private float spawnInterval = 0;
    //  queue implementation due to Udon lacking lists/generics
    private int front, rear = 0;
    private Transform[] activeNoteBlocksQueue;

    public string SongDataString = "";

    // Start is called before the first frame update
    void Start()
    {
        //    //  Create pool of note blocks
        //    InitializeNoteBlockPool();

        ParseAndCacheSongData();
    }

    private void ParseAndCacheSongData()
    {
        //  Parse song data
        SongDataString = SongDataString.Replace(" ", String.Empty);
        Debug.Log(SongDataString);
        string[] notes = SongDataString.Split(new[] { "},{" }, StringSplitOptions.None);

        //  cleanup leading and trailing characters
        notes[0] = notes[0].Replace("{", String.Empty);
        notes[notes.Length - 1] = notes[notes.Length - 1].Replace("}", String.Empty);

        for (int i = 0; i < notes.Length; i++)
        {
            //Debug.Log(notes[i]);
            //Debug.Log("=================");

            //  Parse note data
            string[] noteDatas = notes[i].Split(',');

            //  init cache array
            _time = new float[notes.Length];
            _lineIndex = new int[notes.Length];
            _lineLayer = new int[notes.Length];
            _type = new int[notes.Length];
            _cutDirection = new int[notes.Length];

            for (int j = 0; j < noteDatas.Length; j++)
            {
                //  parse
                //Debug.Log(noteDatas[j]);
                string[] param = noteDatas[j].Split(':');

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

    public void Update()
    {
        if (isSongActive == false)
            return;
        if (noteBlockPool.Length <= 0)
            return;

        //  song timer
        UpdateSongTimer();

        //  spawn noteblocks
        SpawnActiveNoteBlocks();

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

    private void SpawnActiveNoteBlocks()
    {
        spawnInterval -= Time.deltaTime;
        if (spawnInterval <= 0)
        {
            //  spawn with random position
            noteBlockPool[currentActiveSpawnIndex].gameObject.SetActive(true);
            float randX = UnityEngine.Random.Range(-spawnRangeX, spawnRangeX);
            float randY = UnityEngine.Random.Range(-spawnRangeY, spawnRangeY);
            float randZ = UnityEngine.Random.Range(-spawnRangeZ, spawnRangeZ);
            Vector3 pos = transform.position + new Vector3(randX, randY, randZ);
            noteBlockPool[currentActiveSpawnIndex].transform.position = pos;

            //  enqueue
            QueueEnqueue(noteBlockPool[currentActiveSpawnIndex].transform);

            currentActiveSpawnIndex++;
            spawnInterval = songDuration / songNoteAmount;
        }
    }

    private void UpdateSongTimer()
    {
        songTimer -= Time.deltaTime;
        if (songTimer < 0)
        {
            StopSong();
        }
    }

    private void TranslateNoteBlocks()
    {
        for (int i = 0; i < activeNoteBlocksQueue.Length; i++)
        {
            if (activeNoteBlocksQueue[i] == null)
                continue;

            //if (!activeNoteBlocksQueue[i].gameObject.activeInHierarchy)
            //    activeNoteBlocksQueue[i].gameObject.SetActive(true);

            activeNoteBlocksQueue[i].Translate(transform.forward * Time.deltaTime * speed);
        }

        //  Disable on distance
        if (Vector3.Distance(transform.position, QueuePeek().position) >= despawnDistance)
        {
            Transform tr = QueueDequeue();
            tr.gameObject.SetActive(false);
        }
    }

    private void InitializeNoteBlockPool()
    {
        noteBlockPool = new GameObject[poolAmount];

        for (int i = 0; i < poolAmount; i++)
        {
            noteBlockPool[i] = VRCInstantiate(noteBlockPrefab);
        }
    }

    public void StartSong()
    {
        //  Reset queue
        activeNoteBlocksQueue = new Transform[queueCapacity];
        front = 0;
        rear = 0;

        currentActiveSpawnIndex = 0;
        spawnInterval = 0;
        //spawnInterval = songDuration / songNoteAmount;
        //for (int i = 0; i < activeNoteBlocksQueue.Length; i++)
        //{
        //    QueueEnqueue(noteBlockPool[i].transform);
        //    //activeNoteBlocks[i] = noteBlockPool[i].transform;
        //}

        songTimer = songDuration;
        isSongActive = true;
    }

    public void StopSong()
    {
        isSongActive = false;

        DestroyActiveBlocks();
    }

    private void DestroyActiveBlocks()
    {
        //if (activeNoteBlocksQueue.Length <= 0)
        //    return;

        //Transform tr = QueueDequeue();

        //  [BUG] disabling gameobjects from the queue breaks the next start. Mitigation: disable from the pool instead but this is slow
        for (int i = 0; i < noteBlockPool.Length; i++)
        {
            if (!noteBlockPool[i].gameObject.activeInHierarchy)
                continue;

            //Destroy(activeNoteBlocks[i]);
            noteBlockPool[i].gameObject.SetActive(false);
        }
    }

    #region Queue implementation since Udon does not support lists/queues
    // function to insert an element  
    // at the rear of the queue  
    public void QueueEnqueue(Transform data)
    {
        // check queue is full or not  
        if (queueCapacity == rear)
        {
            Debug.Log("\nQueue is full\n");
            return;
        }

        // insert element at the rear  
        else
        {
            activeNoteBlocksQueue[rear] = data;
            rear++;
        }
        return;
    }

    // function to delete an element  
    // from the front of the queue  
    public Transform QueueDequeue()
    {
        Transform dequeued = null;

        // if queue is empty  
        if (front == rear)
        {
            Debug.Log("\nQueue is empty\n");
            return null;
        }

        // shift all the elements from index 2 till rear  
        // to the right by one  
        else
        {
            dequeued = activeNoteBlocksQueue[front];

            for (int i = 0; i < rear - 1; i++)
            {
                activeNoteBlocksQueue[i] = activeNoteBlocksQueue[i + 1];
            }

            // store 0 at rear indicating there's no element  
            if (rear < queueCapacity)
                activeNoteBlocksQueue[rear] = null;

            // decrement rear  
            rear--;
        }

        return dequeued;
    }

    //// print queue elements  
    //public void queueDisplay()
    //{
    //    int i;
    //    if (front == rear)
    //    {
    //        Console.Write("\nQueue is Empty\n");
    //        return;
    //    }

    //    // traverse front to rear and print elements  
    //    for (i = front; i < rear; i++)
    //    {
    //        Console.Write(" {0} <-- ", queue[i]);
    //    }
    //    return;
    //}

    // print front of queue  
    public Transform QueuePeek()
    {
        if (front == rear)
        {
            Debug.Log("\nQueue is Empty\n");
            return null;
        }
        //Console.Write("\nFront Element is: {0}", queue[front]);

        return activeNoteBlocksQueue[front];
    }
    #endregion
}
