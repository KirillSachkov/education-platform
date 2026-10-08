using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProgressService.Domain.Certificates;

namespace ProgressService.Infrastructure.Postgres.Configuration;

public sealed class CourseCertificateConfiguration : IEntityTypeConfiguration<CourseCertificate>
{
    public const string USER_COURSE_INDEX = "ux_course_certificates_user_id_course_id";
    public const string SERIAL_NUMBER_INDEX = "ux_course_certificates_serial_number";

    public void Configure(EntityTypeBuilder<CourseCertificate> builder)
    {
        builder.ToTable("course_certificates");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever()
            .HasColumnName("id");

        builder.Property(x => x.UserId)
            .IsRequired()
            .HasColumnName("user_id");

        builder.Property(x => x.CourseId)
            .IsRequired()
            .HasColumnName("course_id");

        builder.Property(x => x.SerialNumber)
            .IsRequired()
            .HasMaxLength(50)
            .HasColumnName("serial_number");

        builder.Property(x => x.CourseTitle)
            .IsRequired()
            .HasMaxLength(CourseCertificate.MAX_COURSE_TITLE_LENGTH)
            .HasColumnName("course_title");

        builder.Property(x => x.HolderName)
            .IsRequired()
            .HasMaxLength(CourseCertificate.MAX_HOLDER_NAME_LENGTH)
            .HasColumnName("holder_name");

        builder.Property(x => x.IssuedAt)
            .IsRequired()
            .HasColumnName("issued_at");

        // Главный инвариант — один сертификат на пару (user, course).
        builder.HasIndex(x => new { x.UserId, x.CourseId })
            .HasDatabaseName(USER_COURSE_INDEX)
            .IsUnique();

        // Серийник человекочитаемый и уникальный — публичный идентификатор для проверки.
        builder.HasIndex(x => x.SerialNumber)
            .HasDatabaseName(SERIAL_NUMBER_INDEX)
            .IsUnique();
    }
}
