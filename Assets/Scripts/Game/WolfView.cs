using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Animator))]
public class WolfView : MonoBehaviour
{
    private const int ScentRange = 3;
    private const float PatrolCellsPerSecond = 1f;
    private const float RunCellsPerSecond = 2f;

    private static readonly Vector2Int[] Directions =
        { Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down };

    [SerializeField] private LevelData level;
    [SerializeField] private Vector2 gridOrigin;

    private Transform adventurer;
    private Animator animator;
    private Vector2Int cell;
    private Vector2Int target;
    private Vector2Int facing = Vector2Int.down;
    private Vector2Int lastPatrolDirection;
    private Vector2Int? lastScentCell;
    private float cellSize = 1f;
    private bool chasing;
    private string animatedAction;
    private Vector2Int animatedFacing;

    public void Initialize(LevelData boardLevel, Vector2 worldGridOrigin, float worldCellSize,
        AdventurerView adventurerView)
    {
        level = boardLevel;
        gridOrigin = worldGridOrigin;
        cellSize = worldCellSize;
        adventurer = adventurerView ? adventurerView.transform : null;
    }

    private void Start()
    {
        animator = GetComponent<Animator>();
        if (!adventurer)
        {
#if UNITY_2022_2_OR_NEWER
            AdventurerView adventurerView = FindFirstObjectByType<AdventurerView>();
#else
            AdventurerView adventurerView = FindObjectOfType<AdventurerView>();
#endif
            adventurer = adventurerView ? adventurerView.transform : null;
        }

        cell = WorldToCell(transform.position);
        if (level == null || !level.IsCellWalkable(cell))
        {
            Debug.LogWarning("Wolf 需要有效关卡，且出生位置必须位于可通行格内。", this);
            enabled = false;
            return;
        }

        target = cell;
        transform.position = CellCenter(cell);
        PlayAnimation("Idle", facing);
    }

    private void Update()
    {
        UpdateScent();

        float remainingMovement = (chasing ? RunCellsPerSecond : PatrolCellsPerSecond)
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

    private void UpdateScent()
    {
        if (!adventurer)
            return;

        Vector2Int adventurerCell = WorldToCell(adventurer.position);
        int scentDistance = Mathf.Abs(adventurerCell.x - cell.x) + Mathf.Abs(adventurerCell.y - cell.y);
        if (scentDistance <= ScentRange)
        {
            bool startedChasing = !chasing;
            lastScentCell = adventurerCell;
            chasing = true;
            if (startedChasing && cell != target)
                PlayAnimation("Run", target - cell);
        }
    }

    private bool ChooseTarget()
    {
        if (chasing)
        {
            if (lastScentCell.HasValue && cell != lastScentCell.Value)
                return TryChooseChaseStep(lastScentCell.Value);

            lastScentCell = null;
            chasing = false;
            lastPatrolDirection = Vector2Int.zero;
        }

        return ChoosePatrolTarget();
    }

    private bool ChoosePatrolTarget()
    {
        int choices = 0;
        foreach (Vector2Int direction in Directions)
        {
            Vector2Int next = cell + direction;
            if (direction == -lastPatrolDirection || !level.IsCellWalkable(next))
                continue;

            if (Random.Range(0, ++choices) == 0)
                target = next;
        }

        if (choices > 0)
            return true;

        Vector2Int back = cell - lastPatrolDirection;
        if (back == cell || !level.IsCellWalkable(back))
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
                    !level.IsCellWalkableIgnoring(next, GameDataConfig.SceneElement.Grass))
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
        if (action == animatedAction && direction == animatedFacing)
            return;

        animatedAction = action;
        animatedFacing = direction;

        string suffix;
        if (direction.x < 0) suffix = "L";
        else if (direction.x > 0) suffix = "R";
        else if (direction.y > 0) suffix = "D";
        else suffix = "T";

        animator.Play($"Base Layer.Wolf{action}-{suffix}", 0, 0f);
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
