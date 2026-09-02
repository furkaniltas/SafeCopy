using EksimSafeCopy.Benchmark.Models;
using EksimSafeCopy.Core.Models;

namespace EksimSafeCopy.Benchmark.Matching;

public sealed class SpanMatcher
{
    public static List<EntityMatch> MatchRecord(BenchmarkRecord record, List<BenchmarkDetection> detections)
    {
        var matches = new List<EntityMatch>();
        // Only consider supported ground truth for main matching
        var supportedGroundTruth = record.Entities.Where(e => e.MappedType != null && e.MappingStatus != MappingStatus.UNSUPPORTED && e.MappingStatus != MappingStatus.AMBIGUOUS).ToList();
        var detectionPool = detections.ToList(); // all detections, but filter Possible separately for main metrics if needed

        // For main metrics, exclude PossiblePersonalData unless explicitly mapped
        var mainDetections = detectionPool.Where(d => d.Type != DetectionType.PossiblePersonalData).ToList();

        // Build candidate pairs with IoU and overlap
        var candidates = new List<(GroundTruthEntity gt, BenchmarkDetection det, double iou, double overlap, bool exact)>();
        foreach (var gt in supportedGroundTruth)
        {
            foreach (var det in mainDetections)
            {
                if (gt.MappedType != det.Type) continue;
                var overlap = ComputeOverlap(gt.Start, gt.End, det.Start, det.End);
                if (overlap <= 0) continue;
                var iou = ComputeIoU(gt.Start, gt.End, det.Start, det.End);
                bool exact = gt.Start == det.Start && gt.End == det.End;
                candidates.Add((gt, det, iou, overlap, exact));
            }
        }

        // Sort by exact first, then IoU descending, then overlap descending
        candidates.Sort((a,b) => {
            if (a.exact != b.exact) return a.exact ? -1 : 1;
            int cmp = b.iou.CompareTo(a.iou);
            if (cmp != 0) return cmp;
            return b.overlap.CompareTo(a.overlap);
        });

        var matchedGt = new HashSet<GroundTruthEntity>();
        var matchedDet = new HashSet<BenchmarkDetection>();

        foreach (var c in candidates)
        {
            if (matchedGt.Contains(c.gt) || matchedDet.Contains(c.det)) continue;
            matchedGt.Add(c.gt);
            matchedDet.Add(c.det);
            var kind = c.exact ? MatchKind.Exact : MatchKind.Overlap;
            // If overlap but not exact, also consider boundary mismatch if overlap <1 but >0
            if (!c.exact && c.iou < 1.0) kind = MatchKind.BoundaryMismatch;
            matches.Add(new EntityMatch
            {
                GroundTruth = c.gt,
                Detection = c.det,
                Kind = kind,
                OverlapRatio = c.overlap / (c.gt.End - c.gt.Start),
                IoU = c.iou,
                RecordId = record.Id
            });
        }

        // Remaining ground truth = FN
        foreach (var gt in supportedGroundTruth)
        {
            if (!matchedGt.Contains(gt))
            {
                matches.Add(new EntityMatch
                {
                    GroundTruth = gt,
                    Detection = null,
                    Kind = MatchKind.Missed,
                    OverlapRatio = 0,
                    IoU = 0,
                    RecordId = record.Id
                });
            }
        }

        // Remaining detections = FP
        foreach (var det in mainDetections)
        {
            if (!matchedDet.Contains(det))
            {
                matches.Add(new EntityMatch
                {
                    GroundTruth = null,
                    Detection = det,
                    Kind = MatchKind.FalsePositive,
                    OverlapRatio = 0,
                    IoU = 0,
                    RecordId = record.Id
                });
            }
        }

        return matches;
    }

    public static double ComputeOverlap(int gtStart, int gtEnd, int detStart, int detEnd)
    {
        int start = Math.Max(gtStart, detStart);
        int end = Math.Min(gtEnd, detEnd);
        return Math.Max(0, end - start);
    }

    public static double ComputeIoU(int gtStart, int gtEnd, int detStart, int detEnd)
    {
        int overlap = (int)ComputeOverlap(gtStart, gtEnd, detStart, detEnd);
        if (overlap == 0) return 0;
        int gtLen = gtEnd - gtStart;
        int detLen = detEnd - detStart;
        int union = gtLen + detLen - overlap;
        return union == 0 ? 0 : (double)overlap / union;
    }
}
