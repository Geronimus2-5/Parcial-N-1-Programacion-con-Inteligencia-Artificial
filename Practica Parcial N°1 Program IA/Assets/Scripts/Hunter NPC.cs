using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum HunterState { Patrol, Attack, Gather }

public class HunterNPC : MonoBehaviour
{
    [Header("FSM State (Read Only)")]
    public HunterState currentState = HunterState.Patrol;

    [Header("Required Variables")]
    public float TBA = 3f;
    public float rangeAttackRadius = 10f;
    public float meleeAttackRadius = 2.5f;
    public float visionRadius = 12f;

    [Header("Movement & Waypoints")]
    public Transform[] waypoints;
    public float speed = 4f;
    public float waypointStoppingDistance = 1.5f;
    public bool reverseWaypoints = false;
    private int currentWaypointIndex = 0;
    private bool movingForward = true;

    [Header("POI Spawning")]
    public GameObject poiPrefab;
    public float poiSpawnInterval = 6f;
    private float poiSpawnTimer;

    [Header("Gather Settings")]
    public float gatherDuration = 2f;
    private float gatherTimer;

    private float tbaTimer;
    private BoidAgent targetBoid;
    private BoidAgent deadBoidToGather;
    public Vector3 Velocity { get; private set; }

    void Update()
    {
        Vector3 lastPos = transform.position;

        if (tbaTimer > 0) tbaTimer -= Time.deltaTime;

        switch (currentState)
        {
            case HunterState.Patrol:
                UpdatePatrolState();
                CheckPatrolTransitions();
                break;

            case HunterState.Attack:
                UpdateAttackState();
                CheckAttackTransitions();
                break;

            case HunterState.Gather:
                UpdateGatherState();
                CheckGatherTransitions();
                break;
        }

        Velocity = (transform.position - lastPos) / Time.deltaTime;
    }

    private void UpdatePatrolState()
    {
        poiSpawnTimer += Time.deltaTime;
        if (poiSpawnTimer >= poiSpawnInterval)
        {
            poiSpawnTimer = 0f;
            if (GameObject.FindGameObjectsWithTag("POI").Length < 5 && poiPrefab != null)
            {
                Vector3 spawnPos = transform.position + Random.insideUnitSphere * 4f;
                spawnPos.y = transform.position.y;
                Instantiate(poiPrefab, spawnPos, Quaternion.identity);
            }
        }

        if (waypoints == null || waypoints.Length == 0) return;

        Transform targetWp = waypoints[currentWaypointIndex];
        if (targetWp == null) return;

        MoveTowards(targetWp.position);

        Vector3 flatWpPos = new Vector3(targetWp.position.x, transform.position.y, targetWp.position.z);
        if (Vector3.Distance(transform.position, flatWpPos) <= waypointStoppingDistance)
        {
            AdvanceWaypoint();
        }
    }

    private void AdvanceWaypoint()
    {
        if (waypoints.Length <= 1) return;

        if (!reverseWaypoints)
        {
            currentWaypointIndex = (currentWaypointIndex + 1) % waypoints.Length;
        }
        else
        {
            if (movingForward)
            {
                currentWaypointIndex++;
                if (currentWaypointIndex >= waypoints.Length)
                {
                    currentWaypointIndex = waypoints.Length - 2;
                    movingForward = false;
                }
            }
            else
            {
                currentWaypointIndex--;
                if (currentWaypointIndex < 0)
                {
                    currentWaypointIndex = 1;
                    movingForward = true;
                }
            }
        }
    }

    private void CheckPatrolTransitions()
    {
        // Si el temporizador de recarga de ataque ya pasó, buscar presa viva
        if (tbaTimer <= 0)
        {
            targetBoid = FindNearestAliveBoid();
            if (targetBoid != null && Vector3.Distance(transform.position, targetBoid.transform.position) <= visionRadius)
            {
                currentState = HunterState.Attack;
            }
        }
    }

    private void UpdateAttackState()
    {
        if (targetBoid == null || targetBoid.isDead) return;

        float dist = Vector3.Distance(transform.position, targetBoid.transform.position);

        if (dist <= meleeAttackRadius)
        {
            PerformAttack(targetBoid, 100f);
        }
        else if (dist <= rangeAttackRadius)
        {
            PerformAttack(targetBoid, 40f);
        }
        else
        {
            MoveTowards(targetBoid.transform.position);
        }
    }

    private void PerformAttack(BoidAgent target, float damage)
    {
        target.TakeDamage(damage);
        tbaTimer = TBA;

        // Si el ataque mató al Boid, guardamos la referencia para el recolectado inmediato
        if (target.isDead)
        {
            deadBoidToGather = target;
            targetBoid = null;
            gatherTimer = 0f;
            currentState = HunterState.Gather;
        }
        else
        {
            // Si sobrevivió al ataque, vuelve a patrulla mientras espera el TBA
            targetBoid = null;
            currentState = HunterState.Patrol;
        }
    }

    private void CheckAttackTransitions()
    {
        if (targetBoid == null || targetBoid.isDead)
        {
            if (currentState == HunterState.Attack) currentState = HunterState.Patrol;
            return;
        }

        if (Vector3.Distance(transform.position, targetBoid.transform.position) > visionRadius)
        {
            targetBoid = null;
            currentState = HunterState.Patrol;
        }
    }

    private void UpdateGatherState()
    {
        // Control de seguridad: si la presa ya no existe, volvemos a Patrulla inmediatamente
        if (deadBoidToGather == null)
        {
            currentState = HunterState.Patrol;
            return;
        }

        float dist = Vector3.Distance(transform.position, deadBoidToGather.transform.position);

        // Moverse hacia el cadáver si está lejos
        if (dist > 1.2f)
        {
            MoveTowards(deadBoidToGather.transform.position);
        }
        else
        {
            // Acumular tiempo de recolección cuando ya está cerca
            gatherTimer += Time.deltaTime;

            if (gatherTimer >= gatherDuration)
            {
                deadBoidToGather.OnCollected();
                deadBoidToGather = null;
                gatherTimer = 0f;
                currentState = HunterState.Patrol;
            }
        }
    }

    private void CheckGatherTransitions()
    {
        if (deadBoidToGather == null)
        {
            currentState = HunterState.Patrol;
        }
    }

    private void MoveTowards(Vector3 target)
    {
        Vector3 dir = (target - transform.position);
        dir.y = 0;
        if (dir.sqrMagnitude > 0.001f)
        {
            dir = dir.normalized;
            transform.position += dir * speed * Time.deltaTime;
            transform.forward = dir;
        }
    }

    private BoidAgent FindNearestAliveBoid()
    {
        BoidAgent[] boids = FindObjectsOfType<BoidAgent>();
        BoidAgent nearest = null;
        float minDist = Mathf.Infinity;
        foreach (var b in boids)
        {
            if (b.isDead || !b.gameObject.activeInHierarchy) continue;
            float d = Vector3.Distance(transform.position, b.transform.position);
            if (d < minDist) { minDist = d; nearest = b; }
        }
        return nearest;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, visionRadius);

        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, rangeAttackRadius);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, meleeAttackRadius);
    }
}