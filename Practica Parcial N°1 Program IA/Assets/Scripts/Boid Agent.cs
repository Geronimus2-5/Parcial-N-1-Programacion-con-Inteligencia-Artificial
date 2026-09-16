using System.Collections;
using UnityEngine;

public class BoidAgent : MonoBehaviour
{
    [Header("Boid Settings")]
    public float maxSpeed = 5f;
    public float maxForce = 8f;

    [Header("Flocking Radii")]
    public float perceptionRadius = 8f;
    public float separationRadius = 4.5f;

    [Header("Weights")]
    public float separationWeight = 4.5f;
    public float alignmentWeight = 0.8f;
    public float cohesionWeight = 0.3f;

    [Header("Map Boundaries & Wander")]
    public float mapRadius = 35f;
    public float boundaryWeight = 5.0f;
    public float wanderWeight = 2.0f;
    private float wanderAngle;

    [Header("Evade / Danger")]
    public float evadeRadius = 7f;
    public float evadeWeight = 4.0f;
    public HunterNPC hunter;

    [Header("Health & Respawn")]
    public float maxHealth = 100f;
    public float currentHealth;
    public float respawnDelay = 5f;
    public bool isDead = false;
    public bool isBeingGathered = false; // Nueva bandera para evitar bucles en la FSM

    [Header("Interest Object Interaction")]
    public float arrivalRadius = 1.5f;
    public float damageInterval = 0.5f;
    public float damageToPOI = 15f;

    [Header("Auto-Spawn Clones")]
    public bool isOriginal = true;
    public int totalBoidsToSpawn = 6;

    private Vector3 velocity;
    private GameObject currentTargetPOI;
    private float damageTimer;

    void Start()
    {
        currentHealth = maxHealth;
        Vector2 randomDir = Random.insideUnitCircle.normalized;
        velocity = new Vector3(randomDir.x, 0, randomDir.y) * maxSpeed;
        wanderAngle = Random.Range(0f, 360f);

        if (hunter == null) hunter = FindAnyObjectByType<HunterNPC>();

        if (isOriginal)
        {
            isOriginal = false;
            for (int i = 1; i < totalBoidsToSpawn; i++)
            {
                Vector2 spawnOffset = Random.insideUnitCircle * (mapRadius * 0.5f);
                Vector3 spawnPos = new Vector3(spawnOffset.x, transform.position.y, spawnOffset.y);

                GameObject clone = Instantiate(gameObject, spawnPos, Quaternion.identity);
                clone.GetComponent<BoidAgent>().isOriginal = false;
            }
        }
    }

    void Update()
    {
        if (isDead || isBeingGathered) return;

        Vector3 acceleration = Vector3.zero;

        if (hunter != null && Vector3.Distance(transform.position, hunter.transform.position) < evadeRadius)
        {
            Vector3 evadeForce = CalculateEvade(hunter.transform.position, hunter.Velocity) * evadeWeight;
            acceleration += evadeForce;
        }
        else
        {
            FindNearestPOI();

            if (currentTargetPOI != null)
            {
                Vector3 arriveForce = CalculateArrive(currentTargetPOI.transform.position);
                acceleration += arriveForce;

                if (Vector3.Distance(transform.position, currentTargetPOI.transform.position) <= arrivalRadius)
                {
                    damageTimer += Time.deltaTime;
                    if (damageTimer >= damageInterval)
                    {
                        var poiScript = currentTargetPOI.GetComponent<PointOfInterest>();
                        if (poiScript != null) poiScript.TakeDamage(damageToPOI);
                        damageTimer = 0f;
                    }
                }
            }
            else
            {
                Vector3 sep = CalculateSeparation() * separationWeight;
                Vector3 ali = CalculateAlignment() * alignmentWeight;
                Vector3 coh = CalculateCohesion() * cohesionWeight;
                Vector3 wander = CalculateWander() * wanderWeight;

                acceleration += sep + ali + coh + wander;
            }
        }

        acceleration += CalculateBoundaryForce() * boundaryWeight;

        velocity = Vector3.ClampMagnitude(velocity + acceleration * Time.deltaTime, maxSpeed);
        velocity.y = 0;

        transform.position += velocity * Time.deltaTime;

        if (velocity.sqrMagnitude > 0.01f)
            transform.forward = velocity.normalized;
    }

    private Vector3 CalculateBoundaryForce()
    {
        Vector3 centerOffset = Vector3.zero - transform.position;
        centerOffset.y = 0;
        if (centerOffset.magnitude > mapRadius)
        {
            return centerOffset.normalized * maxSpeed - velocity;
        }
        return Vector3.zero;
    }

    private Vector3 CalculateWander()
    {
        wanderAngle += Random.Range(-0.8f, 0.8f);
        Vector3 circleCenter = velocity.normalized * 3f;
        Vector3 displacement = new Vector3(Mathf.Cos(wanderAngle), 0, Mathf.Sin(wanderAngle)) * 2f;
        Vector3 wanderForce = circleCenter + displacement;
        return Vector3.ClampMagnitude(wanderForce - velocity, maxForce);
    }

    private Vector3 CalculateSeparation()
    {
        Vector3 steer = Vector3.zero;
        int count = 0;
        BoidAgent[] boids = FindObjectsOfType<BoidAgent>();

        foreach (var boid in boids)
        {
            if (boid == this || boid.isDead || boid.isBeingGathered) continue;
            float d = Vector3.Distance(transform.position, boid.transform.position);
            if (d < separationRadius && d > 0)
            {
                Vector3 diff = (transform.position - boid.transform.position).normalized / d;
                steer += diff;
                count++;
            }
        }
        if (count > 0) steer /= count;
        if (steer.sqrMagnitude > 0)
        {
            steer = steer.normalized * maxSpeed - velocity;
            steer = Vector3.ClampMagnitude(steer, maxForce);
        }
        return steer;
    }

    private Vector3 CalculateAlignment()
    {
        Vector3 avgVelocity = Vector3.zero;
        int count = 0;
        BoidAgent[] boids = FindObjectsOfType<BoidAgent>();

        foreach (var boid in boids)
        {
            if (boid == this || boid.isDead || boid.isBeingGathered) continue;
            float d = Vector3.Distance(transform.position, boid.transform.position);
            if (d < perceptionRadius)
            {
                avgVelocity += boid.velocity;
                count++;
            }
        }
        if (count > 0)
        {
            avgVelocity /= count;
            Vector3 steer = (avgVelocity.normalized * maxSpeed) - velocity;
            return Vector3.ClampMagnitude(steer, maxForce);
        }
        return Vector3.zero;
    }

    private Vector3 CalculateCohesion()
    {
        Vector3 center = Vector3.zero;
        int count = 0;
        BoidAgent[] boids = FindObjectsOfType<BoidAgent>();

        foreach (var boid in boids)
        {
            if (boid == this || boid.isDead || boid.isBeingGathered) continue;
            float d = Vector3.Distance(transform.position, boid.transform.position);
            if (d < perceptionRadius)
            {
                center += boid.transform.position;
                count++;
            }
        }
        if (count > 0)
        {
            center /= count;
            return CalculateArrive(center);
        }
        return Vector3.zero;
    }

    private Vector3 CalculateEvade(Vector3 targetPos, Vector3 targetVel)
    {
        float lookAhead = Vector3.Distance(targetPos, transform.position) / maxSpeed;
        Vector3 predictedPos = targetPos + targetVel * lookAhead;
        Vector3 desired = (transform.position - predictedPos).normalized * maxSpeed;
        return Vector3.ClampMagnitude(desired - velocity, maxForce);
    }

    private Vector3 CalculateArrive(Vector3 target)
    {
        Vector3 desired = target - transform.position;
        desired.y = 0;
        float distance = desired.magnitude;
        if (distance < arrivalRadius)
            desired = desired.normalized * maxSpeed * (distance / arrivalRadius);
        else
            desired = desired.normalized * maxSpeed;

        return Vector3.ClampMagnitude(desired - velocity, maxForce);
    }

    private void FindNearestPOI()
    {
        GameObject[] pois = GameObject.FindGameObjectsWithTag("POI");
        float nearestDist = Mathf.Infinity;
        currentTargetPOI = null;

        foreach (var poi in pois)
        {
            float d = Vector3.Distance(transform.position, poi.transform.position);
            if (d < perceptionRadius && d < nearestDist)
            {
                nearestDist = d;
                currentTargetPOI = poi;
            }
        }
    }

    public void TakeDamage(float amount)
    {
        if (isDead || isBeingGathered) return;
        currentHealth -= amount;
        if (currentHealth <= 0)
        {
            currentHealth = 0;
            isDead = true;
        }
    }

    public void OnCollected()
    {
        if (isBeingGathered) return;
        isBeingGathered = true;
        StartCoroutine(RespawnRoutine());
    }

    private IEnumerator RespawnRoutine()
    {
        Renderer r = GetComponent<Renderer>();
        Collider c = GetComponent<Collider>();
        if (r != null) r.enabled = false;
        if (c != null) c.enabled = false;

        yield return new WaitForSeconds(respawnDelay);

        Vector2 randomPoint = Random.insideUnitCircle * (mapRadius * 0.7f);
        transform.position = new Vector3(randomPoint.x, transform.position.y, randomPoint.y);

        currentHealth = maxHealth;
        isDead = false;
        isBeingGathered = false; // Reactivamos las banderas al reaparecer

        if (r != null) r.enabled = true;
        if (c != null) c.enabled = true;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, perceptionRadius);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, separationRadius);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(Vector3.zero, mapRadius);
    }
}