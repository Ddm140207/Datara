using Datara.Application.Datasets.Dtos;
using Datara.Domain.Entities;

namespace Datara.Application.Datasets.Services;

/// <summary>Projects the readiness score entity into its DTO form.</summary>
internal static class MlReadinessMapper
{
    public static MlReadinessDto ToDto(DatasetAnalysis analysis) => new()
    {
        Score = analysis.MlReadinessScore,
        DataQualityScore = analysis.DataQualityScore,
        Grade = analysis.Grade,
        Verdict = analysis.Verdict,
        Components = analysis.ScoreComponents
            .Select(c => new ScoreComponentDto
            {
                Key = c.Key,
                Label = c.Label,
                Score = c.Score,
                Weight = c.Weight,
                Rationale = c.Rationale,
                Penalties = c.Penalties
                    .Select(p => new ScorePenaltyDto
                    {
                        Points = p.Points,
                        Reason = p.Reason,
                        Detail = p.Detail,
                    })
                    .ToList(),
            })
            .ToList(),
        Strengths = analysis.Strengths,
        Risks = analysis.Risks,
        Gates = analysis.Gates,
    };
}