using SafeCopy.Core.Models;
using SafeCopy.Detectors.Detection.Normalization;
using DetectionModel = SafeCopy.Core.Models.Detection;

namespace SafeCopy.Detectors.Detection.PossiblePersonalData;

public interface IPossiblePersonalDataAnalyzer
{
    IReadOnlyList<DetectionModel> Analyze(Document document, NormalizedText normalizedText, IReadOnlyList<DetectionModel> existingDetections, CancellationToken cancellationToken = default);
}
