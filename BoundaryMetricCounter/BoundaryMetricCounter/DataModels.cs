using System.Collections.Generic;

namespace BoundaryMetricCounter
{
    // Для Таблицы 3/5 (Свойства подграфов)
    public class SubgraphRow
    {
        public int SelectionNodeId { get; set; }
        public string TransitionIds { get; set; } = "";
        public int CorrectedComplexity { get; set; }
        public string SubgraphNodes { get; set; } = "";
        public int LowerBoundId { get; set; }
    }

    // Для Таблицы 4/6 (Скорректированные сложности вершин)
    public class NodeComplexityRow
    {
        public int NodeId { get; set; }
        public int Complexity { get; set; }
    }
}