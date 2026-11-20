using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Animator))]
public abstract class MonsterPatrolView : MonoBehaviour
{
    [SerializeField, Min(0)] private int visionRange = 3;
    [SerializeField, Min(0)] private int hearingRange = 3;
    [SerializeField, Min(0)] private int smellRange = 3;
    protected abstract string AnimationPrefix { get; }
    protected virtual float ChaseSpeed => 1f;
    [SerializeField] private bool patrolAllowsPassableElements = true;
    [SerializeField] private bool chaseAllowsPassableElements = true;

    private static readonly Vector2Int[] Directions =
        { Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down };

    [SerializeField] private LevelData level;
    [SerializeField] private Vector2 gridOrigin;

    private AdventurerView adventurer;
    private readonly Queue<Vector2Int> wave = new Queue<Vector2Int>();
    private readonly HashSet<Vector2Int> visited = new HashSet<Vector2Int>();
    private Animator animator;
    private Vector2Int cell;
    private Vector2Int target;
    private Vector2Int facing = Vector2Int.down;
    private Vector2Int lastPatrolDirection;
    private Vector2Int? lastDetectedCell;
    private float cellSize = 1f;
    private bool chasing;
    private bool directDetection;
    private bool followingScent;
    private string animatedAction;
    private Vector2Int animatedFacing;

    public void Initialize(LevelData boardLevel, Vector2 worldGridOrigin, float worldCellSize = 1f,
        AdventurerView adventurerView = null)
    {
        level = boardLevel;
        gridOrigin = worldGridOrigin;
        cellSize = worldCellSize;
        adventurer = adventurerView;
    }

    protected void Start()
    {
        animator = GetComponent<Animator>();
        if (!adventurer)
        {
#if UNITY_2022_2_OR_NEWER
            AdventurerView adventurerView = FindFirstObjectByType<AdventurerView>();
#else
            AdventurerView adventurerView = FindObjectOfType<AdventurerView>();
#endif
            adventurer = adventurerView;
        }

        cell = WorldToCell(transform.position);
        if (level == null || !level.IsCellWalkable(cell))
        {
            Debug.LogWarning("怪物需要有效关卡，且出生位置必须位于可通行格内。", this);
            enabled = false;
            return;
        }

        target = cell;
        transform.position = CellCenter(cell);
        PlayAnimation("Idle", facing);
    }

    protected void Update()
    {
        UpdatePerception();

        float remainingMovement = (chasing ? ChaseSpeed : 1f)
            * cellSize * Time.deltaTime;
        while (remainingMovement > 0f)
        {
            if (cell == target)
            {
                if (!ChooseTarget())
                {
                    PlayAnimation("Idle", facing);
                    return;
                }

                facing = target - cell;
                PlayAnimation(chasing ? "Run" : "Move", facing);
            }

            Vector3 destination = CellCenter(target);
            float distance = Vector3.Distance(transform.position, destination);
            transform.position = Vector3.MoveTowards(transform.position, destination, remainingMovement);
            if (remainingMovement < distance)
                return;

            remainingMovement -= distance;
            Vector2Int direction = target - cell;
            cell = target;
            if (!chasing)
                lastPatrolDirection = direction;
        }
    }

    private void UpdatePerception()
    {
        directDetection = false;
        if (!adventurer)
            return;

        Vector2Int origin = WorldToCell(transform.position);
        Vector2Int adventurerCell = WorldToCell(adventurer.transform.position);
        Vector2Int delta = adventurerCell - origin;
        int distance = Mathf.Abs(delta.x) + Mathf.Abs(delta.y);
        if (CanSee(origin, delta) ||
            (adventurer.IsMoving && hearingRange > 0 && distance <= hearingRange &&
             SignalReaches(origin, adventurerCell, hearingRange, true)))
        {
            directDetection = true;
            followingScent = false;
            bool startedChasing = !chasing;
            lastDetectedCell = adventurerCell;
            chasing = true;
            if (startedChasing && cell != target)
                PlayAnimation("Run", target - cell);
        }
    }

    private bool CanSee(Vector2Int origin, Vector2Int delta)
    {
        if (visionRange <= 0) return false;
        int depth = delta.x * facing.x + delta.y * facing.y;
        int width = Mathf.Abs(delta.x);
        int height = Mathf.Abs(delta.y);
        if (depth < 0 || width + height > visionRange) return false;

        // 沿两个格子中心之间的直线，依次检查经过的格子。
        var stepX = new Vector2Int(System.Math.Sign(delta.x), 0);
        var stepY = new Vector2Int(0, System.Math.Sign(delta.y));
        Vector2Int current = origin;
        int x = 0, y = 0;
        while (x < width || y < height)
        {
            int nextX = (2 * x + 1) * height;
            int nextY = (2 * y + 1) * width;
            // 恰好经过格角时，两侧都阻挡才封住这条斜向视线。
            if (nextX == nextY && level.BlocksSight(current + stepX) && level.BlocksSight(current + stepY))
                return false;
            if (nextX <= nextY) { current += stepX; x++; }
            if (nextY <= nextX) { current += stepY; y++; }
            if (level.BlocksSight(current)) return false;
        }
        return true;
    }

    private bool SignalReaches(Vector2Int origin, Vector2Int source, int range, bool sound)
    {
        wave.Clear();
        visited.Clear();
        wave.Enqueue(source);
        visited.Add(source);
        for (int depth = 0; depth <= range && wave.Count > 0; depth++)
        {
            int count = wave.Count;
            while (count-- > 0)
            {
                Vector2Int current = wave.Dequeue();
                if (current == origin && current == source) return true;
                if (sound ? level.BlocksHearing(current) : level.BlocksSmell(current)) continue;
                if (current == origin) return true;
                if (depth == range) continue;
                foreach (var direction in Directions)
                {
                    var next = current + direction;
                    if (level.ContainsCell(next) && visited.Add(next)) wave.Enqueue(next);
                }
            }
        }
        return false;
    }

    private bool ChooseTarget()
    {
        if (!directDetection && TryFollowScent()) return target != cell;
        if (followingScent)
        {
            followingScent = false;
            chasing = false;
            lastDetectedCell = null;
        }
        if (chasing)
        {
            if (lastDetectedCell.HasValue && cell != lastDetectedCell.Value)
            {
                if (TryChooseChaseStep(lastDetectedCell.Value)) return true;
            }
            else if (adventurer && lastDetectedCell == WorldToCell(adventurer.transform.position))
                return false;

            lastDetectedCell = null;
            chasing = false;
            lastPatrolDirection = Vector2Int.zero;
        }

        return ChoosePatrolTarget();
    }

    private bool TryFollowScent()
    {
        if (!adventurer || smellRange <= 0 || level.BlocksSmell(cell)) return false;
        var trail = adventurer.ScentTrail;
        int entrance = -1;
        int nearest = int.MaxValue;
        for (int i = trail.Count - 1; i >= 0; i--)
        {
            var mark = trail[i];
            if (Time.time - mark.time >= AdventurerView.ScentLifetime) break;
            var offset = mark.cell - cell;
            int distance = Mathf.Abs(offset.x) + Mathf.Abs(offset.y);
            if (distance > smellRange || distance >= nearest) continue;
            if (distance != 0 && !level.CanMonsterEnter(mark.cell, chaseAllowsPassableElements)) continue;
            if (!SignalReaches(cell, mark.cell, smellRange, false)) continue;
            entrance = i;
            nearest = distance;
            if (distance == 0) break;
        }
        if (entrance < 0) return false;
        if (nearest == 0)
        {
            // 已经踩上足迹，只走冒险者实际留下的下一格。
            if (entrance + 1 == trail.Count)
            {
                followingScent = chasing = true;
                lastDetectedCell = null;
                target = cell;
                return true;
            }
            Vector2Int next = trail[entrance + 1].cell;
            var delta = next - cell;
            if (Mathf.Abs(delta.x) + Mathf.Abs(delta.y) != 1 || level.BlocksSmell(next) ||
                !level.CanMonsterEnter(next, chaseAllowsPassableElements)) return false;
            target = next;
        }
        // 远处虽有气味，脚边没有连续足迹时不能穿过无气味的空格追击。
        else if (nearest == 1) target = trail[entrance].cell;
        else return false;
        followingScent = chasing = true;
        lastDetectedCell = null;
        return true;
    }

    private bool ChoosePatrolTarget()
    {
        int choices = 0;
        foreach (Vector2Int direction in Directions)
        {
            Vector2Int next = cell + direction;
            if (direction == -lastPatrolDirection || !level.CanMonsterEnter(next, patrolAllowsPassableElements))
                continue;

            if (Random.Range(0, ++choices) == 0)
                target = next;
        }

        if (choices > 0)
            return true;

        Vector2Int back = cell - lastPatrolDirection;
        if (back == cell || !level.CanMonsterEnter(back, patrolAllowsPassableElements))
            return false;

        target = back;
        return true;
    }

    private bool TryChooseChaseStep(Vector2Int destination)
    {
        var frontier = new Queue<Vector2Int>();
        var parent = new Dictionary<Vector2Int, Vector2Int>();
        frontier.Enqueue(cell);
        parent[cell] = cell;

        while (frontier.Count > 0)
        {
            Vector2Int current = frontier.Dequeue();
            if (current == destination)
                break;

            foreach (Vector2Int direction in Directions)
            {
                Vector2Int next = current + direction;
                if (parent.ContainsKey(next) ||
                    !level.CanMonsterEnter(next, chaseAllowsPassableElements))
                    continue;

                parent[next] = current;
                frontier.Enqueue(next);
            }
        }

        if (!parent.ContainsKey(destination))
            return false;

        Vector2Int step = destination;
        while (parent[step] != cell)
            step = parent[step];

        target = step;
        return true;
    }

    private void PlayAnimation(string action, Vector2Int direction)
    {
        if (AnimationPrefix == "Slime")
        {
            animator.speed = action == "Idle" ? 0f : 1f;
            action = "Move";
        }
        if (action == animatedAction && direction == animatedFacing)
            return;

        animatedAction = action;
        animatedFacing = direction;

        string suffix;
        if (direction.x < 0) suffix = "L";
        else if (direction.x > 0) suffix = "R";
        else if (direction.y > 0) suffix = "D";
        else suffix = "T";

        animator.Play($"Base Layer.{AnimationPrefix}{action}-{suffix}", 0, 0f);
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
}
