using UnityEngine;

public class Spawner : MonoBehaviour
{
    public GameObject prefabToSpawn;
    public Transform spawnPointHolder;

    private Transform[] spawnPoints;

    void Start()
    {
        int count = spawnPointHolder.childCount;
        spawnPoints = new Transform[count];

        for (int i = 0; i < count; i++)
        {
            spawnPoints[i] = spawnPointHolder.GetChild(i);
        }

        SpawnPrefab();

        void SpawnPrefab()
        {
            if (spawnPoints.Length == 0 || prefabToSpawn == null)
            {
                Debug.LogWarning("You fucked something up");
                return;
            }
        }


        // Pick random spawn point
        Transform chosenPoint = spawnPoints[Random.Range(0, spawnPoints.Length)];

        // Spawn prefab
        Instantiate(
            prefabToSpawn,
            chosenPoint.position,
            chosenPoint.rotation
        );
    }
}
