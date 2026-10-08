using EducationContentService.Domain.Roadmaps;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EducationContentService.Infrastructure.Postgres.Configurations;

public class RoadmapEdgeConfiguration : IEntityTypeConfiguration<RoadmapEdge>
{
    public void Configure(EntityTypeBuilder<RoadmapEdge> builder)
    {
        builder.ToTable("roadmap_edges");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever()
            .HasColumnName("id");

        builder.Property(x => x.RoadmapId)
            .IsRequired()
            .HasColumnName("roadmap_id");

        builder.Property(x => x.SourceNodeId)
            .IsRequired()
            .HasColumnName("source_node_id");

        builder.Property(x => x.TargetNodeId)
            .IsRequired()
            .HasColumnName("target_node_id");

        builder.Property(x => x.Label)
            .HasMaxLength(200)
            .HasColumnName("label")
            .IsRequired(false);

        builder.Property(x => x.EdgeType)
            .HasMaxLength(50)
            .HasColumnName("edge_type")
            .IsRequired();

        builder.Property(x => x.Animated)
            .IsRequired()
            .HasColumnName("animated");

        builder.Property(x => x.SourceHandle)
            .HasMaxLength(50)
            .HasColumnName("source_handle")
            .IsRequired(false);

        builder.Property(x => x.TargetHandle)
            .HasMaxLength(50)
            .HasColumnName("target_handle")
            .IsRequired(false);

        builder.HasIndex(x => x.RoadmapId);
    }
}
