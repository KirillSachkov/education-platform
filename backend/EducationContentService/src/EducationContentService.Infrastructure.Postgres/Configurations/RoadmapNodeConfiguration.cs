using EducationContentService.Domain.Roadmaps;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EducationContentService.Infrastructure.Postgres.Configurations;

public class RoadmapNodeConfiguration : IEntityTypeConfiguration<RoadmapNode>
{
    public void Configure(EntityTypeBuilder<RoadmapNode> builder)
    {
        builder.ToTable("roadmap_nodes");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever()
            .HasColumnName("id");

        builder.Property(x => x.RoadmapId)
            .IsRequired()
            .HasColumnName("roadmap_id");

        builder.Property(x => x.NodeType)
            .HasConversion<string>()
            .HasMaxLength(50)
            .HasColumnName("node_type")
            .IsRequired();

        builder.Property(x => x.PositionX)
            .IsRequired()
            .HasColumnName("position_x");

        builder.Property(x => x.PositionY)
            .IsRequired()
            .HasColumnName("position_y");

        builder.Property(x => x.Width)
            .HasColumnName("width")
            .IsRequired(false);

        builder.Property(x => x.Height)
            .HasColumnName("height")
            .IsRequired(false);

        builder.Property(x => x.ParentNodeId)
            .HasColumnName("parent_node_id")
            .IsRequired(false);

        builder.Property(x => x.Data)
            .HasColumnType("jsonb")
            .HasColumnName("data")
            .IsRequired();

        builder.Property(x => x.SortOrder)
            .IsRequired()
            .HasColumnName("sort_order");

        builder.HasIndex(x => x.RoadmapId);
        builder.HasIndex(x => x.ParentNodeId);
    }
}
