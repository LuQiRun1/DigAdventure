using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Animator))]
public class AdventurerView : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float moveSpeed = 4f;
    [SerializeField, Min(0.05f)] private float holdDelay = 0.2f;
    private float pressedAt;
    private bool trackingPress;
    private bool following;
    private Vector2Int? pointerCell;

    private static readonly Vector2Int[] Directions =
        { Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down };

    private readonly Queue<Vector2Int> path = new Queue<Vector2Int>();
    private LevelData level;
    private Camera inputCamera;
    private Animator animator;
    private Vector2 gridOrigin;
    private Vector2Int cell;
    private Vector2Int stepCell;
    private float cellSize = 1f;
    private bool movingToStep;
    public bool IsMoving => movingToStep;
    public struct ScentMark
    {
        public Vector2Int cell;
        public float time;
    }
    public const float ScentLifetime = 3f;
    private readonly List<ScentMark> scentTrail = new List<ScentMark>();
    public IReadOnlyList<ScentMark> ScentTrail => scentTrail;

    private void LeaveScent(Vector2Int position)
    {
        float now = Time.time;
        int last = scentTrail.Count - 1;
        var mark = new ScentMark { cell = position, time = now };
        if (last >= 0 && scentTrail[last].cell == position)
            scentTrail[last] = mark;
        else
            scentTrail.Add(mark);
        while (scentTrail.Count > 0 && now - scentTrail[0].time >= ScentLifetime)
            scentTrail.RemoveAt(0);
    }
    private Vector2Int? moveTarget;
    private bool digOnArrival;
    private Vector2Int facing = Vector2Int.down;
    private Vector2Int animatedFacing;
    private string animatedAction;
    private int animatedState;

    public void Initialize(LevelData boardLevel, Vector2 worldGridOrigin, float worldCellSize = 1f)
    {
        level = boardLevel;
        gridOrigin = worldGridOrigin;
        cellSize = worldCellSize;
        inputCamera = Camera.main;
    }

    private void Start()
    {
        animator = GetComponent<Animator>();
        if (inputCamera == null)
            inputCamera = Camera.main;

        cell = WorldToCell(transform.position);
        if (level == null || !level.IsCellWalkable(cell))
        {
            Debug.LogWarning("Adventurer 需要有效关卡，且出生位置必须位于可通行格内。", this);
            enabled = false;
            return;
        }

        transform.position = CellCenter(cell);
        LeaveScent(cell);
        PlayAnimation("Idle");
    }

    private void Update()
    {
        UpdatePointerMovement();
        LeaveScent(WorldToCell(transform.position));

        if (animatedAction == "Dig")
        {
            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
            if (state.fullPathHash != animatedState || state.normalizedTime < 1f)
                return;
        }

        MoveAlongPath();
        if (movingToStep || path.Count > 0)
        {
            PlayAnimation("Move");
            return;
        }

        // 只在真正到达点击格时挖掘，最近可达格仍然只是移动的备选终点。
        bool shouldDig = digOnArrival && moveTarget == cell;
        if (shouldDig && trackingPress && !following)
        {
            PlayAnimation("Idle");
            return;
        }
        moveTarget = null;
        digOnArrival = false;
        PlayAnimation(shouldDig ? "Dig" : "Idle");
    }

    private void UpdatePointerMovement()
    {
        if (Input.GetMouseButtonDown(0))
        {
            trackingPress = true;
            following = false;
            pressedAt = Time.unscaledTime;
            pointerCell = null;
            HandlePointerCell(Input.mousePosition, true);
        }
        else if (trackingPress && Input.GetMouseButton(0))
        {
            if (Time.unscaledTime - pressedAt < holdDelay) return;
            following = true;
            digOnArrival = false;
            HandlePointerCell(Input.mousePosition, false);
        }
        else if (trackingPress)
        {
            if (following)
            {
                path.Clear();
                moveTarget = null;
            }
            trackingPress = false;
            following = false;
        }
    }

    private void HandlePointerCell(Vector3 screenPosition, bool click)
    {
        if (inputCamera == null)
            return;

        var plane = new Plane(Vector3.forward, transform.position);
        Ray ray = inputCamera.ScreenPointToRay(screenPosition);
        if (!plane.Raycast(ray, out float distance))
            return;

        Vector2Int requestedCell = WorldToCell(ray.GetPoint(distance));
        if (!level.ContainsCell(requestedCell))
        {
            if (!click && pointerCell.HasValue)
            {
                path.Clear();
                moveTarget = null;
                pointerCell = null;
            }
            return;
        }

        if (!click && pointerCell == requestedCell) return;
        pointerCell = requestedCell;

        if (moveTarget == requestedCell)
        {
            digOnArrival = click;
            return;
        }

        moveTarget = requestedCell;
        digOnArrival = click && requestedCell == WorldToCell(transform.position);

        if (digOnArrival && movingToStep && requestedCell == cell)
        {
            // 还在出发格内，沿当前线段返回格中心后挖掘。
            cell = stepCell;
            stepCell = requestedCell;
            facing = stepCell - cell;
            path.Clear();
            return;
        }

        // 改变目的地时先走完当前格，避免角色在两格之间突然转向并斜穿障碍。
        Vector2Int start = movingToStep ? stepCell : cell;
        List<Vector2Int> newPath = FindPathToClosestReachable(start, requestedCell);
        path.Clear();
        for (int i = 0; i < newPath.Count; i++)
            path.Enqueue(newPath[i]);
    }

    private List<Vector2Int> FindPathToClosestReachable(Vector2Int start, Vector2Int requested)
    {
        var frontier = new Queue<Vector2Int>();
        var parent = new Dictionary<Vector2Int, Vector2Int>();
        var distanceFromStart = new Dictionary<Vector2Int, int>();
        frontier.Enqueue(start);
        parent[start] = start;
        distanceFromStart[start] = 0;

        Vector2Int best = start;
        int bestTargetDistance = SquaredDistance(start, requested);
        int bestPathDistance = 0;

        while (frontier.Count > 0)
        {
            Vector2Int current = frontier.Dequeue();
            int targetDistance = SquaredDistance(current, requested);
            int pathDistance = distanceFromStart[current];
            if (targetDistance < bestTargetDistance ||
                (targetDistance == bestTargetDistance && pathDistance < bestPathDistance))
            {
                best = current;
                bestTargetDistance = targetDistance;
                bestPathDistance = pathDistance;
            }

            if (current == requested)
            {
                best = current;
                break;
            }

            for (int i = 0; i < Directions.Length; i++)
            {
                Vector2Int next = current + Directions[i];
                if (parent.ContainsKey(next) || !level.IsCellWalkable(next))
                    continue;

                parent[next] = current;
                distanceFromStart[next] = pathDistance + 1;
                frontier.Enqueue(next);
            }
        }

        var result = new List<Vector2Int>();
        while (best != start)
        {
            result.Add(best);
            best = parent[best];
        }
        result.Reverse();
        return result;
    }

    private void MoveAlongPath()
    {
        float remainingMovement = moveSpeed * cellSize * Time.deltaTime;
        while (remainingMovement > 0f)
        {
            if (!movingToStep)
            {
                if (path.Count == 0)
                    return;

                stepCell = path.Dequeue();
                movingToStep = true;
                facing = stepCell - cell;
            }

            Vector3 destination = CellCenter(stepCell);
            float distance = Vector3.Distance(transform.position, destination);
            transform.position = Vector3.MoveTowards(transform.position, destination, remainingMovement);
            LeaveScent(WorldToCell(transform.position));
            if (remainingMovement < distance)
                return;

            remainingMovement -= distance;
            cell = stepCell;
            movingToStep = false;
        }
    }

    private void PlayAnimation(string action)
    {
        if (action != "Dig" && facing == animatedFacing && action == animatedAction)
            return;

        animatedFacing = facing;
        animatedAction = action;

        string direction;
        if (facing.x < 0) direction = "L";
        else if (facing.x > 0) direction = "R";
        // 关卡行号向下递增。
        else if (facing.y > 0) direction = "D";
        else direction = "T";

        animatedState = Animator.StringToHash($"Base Layer.Adventurer{action}-{direction}");
        animator.Play(animatedState, 0, 0f);
    }

    private Vector2Int WorldToCell(Vector3 position)
    {
        return new Vector2Int(Mathf.RoundToInt((position.x - gridOrigin.x) / cellSize),
            Mathf.RoundToInt((gridOrigin.y - position.y) / cellSize));
    }

    private Vector3 CellCenter(Vector2Int coordinate)
    {
        return new Vector3(gridOrigin.x + coordinate.x * cellSize,
            gridOrigin.y - coordinate.y * cellSize, transform.position.z);
    }

    private static int SquaredDistance(Vector2Int a, Vector2Int b)
    {
        int x = a.x - b.x;
        int y = a.y - b.y;
        return x * x + y * y;
    }
}
