using System;
using System.Collections.Generic;
using Leccionario.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Leccionario.Api.Infrastructure.DbContexts;

public partial class sigafi_esContext : DbContext
{
    public sigafi_esContext(DbContextOptions<sigafi_esContext> options)
        : base(options)
    {
    }

    public virtual DbSet<alumnos> alumnos { get; set; }

    public virtual DbSet<asignaciones_profesores> asignaciones_profesores { get; set; }

    public virtual DbSet<asignaturas> asignaturas { get; set; }

    public virtual DbSet<carreras> carreras { get; set; }

    public virtual DbSet<cplec_asistencias> cplec_asistencias { get; set; }

    public virtual DbSet<cplec_asistencias_historial> cplec_asistencias_historial { get; set; }

    public virtual DbSet<cplec_sesiones> cplec_sesiones { get; set; }

    public virtual DbSet<cursos> cursos { get; set; }

    public virtual DbSet<espacios> espacios { get; set; }

    public virtual DbSet<fechas_horarios> fechas_horarios { get; set; }

    public virtual DbSet<gest_audit_registros> gest_audit_registros { get; set; }

    public virtual DbSet<horario_detalle> horario_detalle { get; set; }

    public virtual DbSet<horas_clases> horas_clases { get; set; }

    public virtual DbSet<matriculas> matriculas { get; set; }

    public virtual DbSet<matriculas_asistencias> matriculas_asistencias { get; set; }

    public virtual DbSet<modalidades> modalidades { get; set; }

    public virtual DbSet<periodos> periodos { get; set; }

    public virtual DbSet<profesores> profesores { get; set; }

    public virtual DbSet<rbac_modulos> rbac_modulos { get; set; }

    public virtual DbSet<rbac_modulos_operaciones> rbac_modulos_operaciones { get; set; }

    public virtual DbSet<rbac_operaciones> rbac_operaciones { get; set; }

    public virtual DbSet<rbac_refresh_tokens> rbac_refresh_tokens { get; set; }

    public virtual DbSet<rbac_rol> rbac_rol { get; set; }

    public virtual DbSet<rbac_rol_modulo_operacion> rbac_rol_modulo_operacion { get; set; }

    public virtual DbSet<rbac_sistema> rbac_sistema { get; set; }

    public virtual DbSet<rbac_usuario_rol> rbac_usuario_rol { get; set; }

    public virtual DbSet<secciones> secciones { get; set; }

    public virtual DbSet<usuarios> usuarios { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .UseCollation("latin1_swedish_ci")
            .HasCharSet("latin1");

        modelBuilder.Entity<alumnos>(entity =>
        {
            entity.HasKey(e => e.idAlumno).HasName("PRIMARY");

            entity.HasIndex(e => e.IdEstadoCivil, "fk_alumnos_estadoCivil_idx");

            entity.HasIndex(e => e.idGeneroAlumno, "fk_alumnos_genero_idx");

            entity.HasIndex(e => e.idNacionalidadEtnica, "fk_alumnos_nacionalidad_idx");

            entity.HasIndex(e => e.idParroquiaResidencia, "fk_alumnos_parroquiaResidencia_idx");

            entity.Property(e => e.idAlumno)
                .HasMaxLength(14)
                .HasDefaultValueSql("''");
            entity.Property(e => e.IdEstadoCivil).HasColumnType("int(11)");
            entity.Property(e => e.apellidoMaterno).HasMaxLength(30);
            entity.Property(e => e.apellidoPaterno).HasMaxLength(30);
            entity.Property(e => e.archivofoto).HasMaxLength(100);
            entity.Property(e => e.barrio_residencia).HasMaxLength(150);
            entity.Property(e => e.carnet_conadis).HasMaxLength(20);
            entity.Property(e => e.celular).HasMaxLength(20);
            entity.Property(e => e.ciudad_Nacimiento).HasMaxLength(30);
            entity.Property(e => e.ciudad_residencia).HasMaxLength(100);
            entity.Property(e => e.direccion).HasMaxLength(60);
            entity.Property(e => e.email).HasMaxLength(40);
            entity.Property(e => e.email_institucional).HasMaxLength(100);
            entity.Property(e => e.fecha_Inscripcion)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp");
            entity.Property(e => e.fotoAprobada).HasColumnType("tinyint(4)");
            entity.Property(e => e.idDiscapacidad).HasColumnType("int(11)");
            entity.Property(e => e.idEtnia).HasColumnType("int(11)");
            entity.Property(e => e.idGeneroAlumno).HasColumnType("int(11)");
            entity.Property(e => e.idInstitucion).HasColumnType("int(11)");
            entity.Property(e => e.idModalidad).HasColumnType("int(11)");
            entity.Property(e => e.idNacionalidad).HasColumnType("int(11)");
            entity.Property(e => e.idNacionalidadEtnica).HasColumnType("int(11)");
            entity.Property(e => e.idNivel)
                .HasDefaultValueSql("'1'")
                .HasColumnType("int(11)");
            entity.Property(e => e.idParroquiaResidencia).HasColumnType("int(11)");
            entity.Property(e => e.idPeriodo)
                .HasMaxLength(7)
                .IsFixedLength();
            entity.Property(e => e.idSeccion).HasColumnType("int(11)");
            entity.Property(e => e.nacionalidad).HasMaxLength(50);
            entity.Property(e => e.nacionalidad_madre).HasMaxLength(150);
            entity.Property(e => e.nacionalidad_padre).HasMaxLength(30);
            entity.Property(e => e.nombre_madre).HasMaxLength(150);
            entity.Property(e => e.nombre_padre).HasMaxLength(150);
            entity.Property(e => e.ocupacion_madre).HasMaxLength(150);
            entity.Property(e => e.ocupacion_padre).HasMaxLength(150);
            entity.Property(e => e.parroquia_nacimiento).HasMaxLength(100);
            entity.Property(e => e.parroquia_residencia).HasMaxLength(150);
            entity.Property(e => e.password).HasMaxLength(20);
            entity.Property(e => e.porcentaje_discapacidad).HasColumnType("int(11)");
            entity.Property(e => e.primerIngreso)
                .HasDefaultValueSql("'1'")
                .HasColumnType("tinyint(4)");
            entity.Property(e => e.primerNombre).HasMaxLength(30);
            entity.Property(e => e.provincia_Nacimiento).HasMaxLength(40);
            entity.Property(e => e.segundoNombre).HasMaxLength(30);
            entity.Property(e => e.sexo)
                .HasMaxLength(1)
                .IsFixedLength();
            entity.Property(e => e.telefono).HasMaxLength(20);
            entity.Property(e => e.tipoDocumento)
                .HasMaxLength(1)
                .IsFixedLength();
            entity.Property(e => e.tipoInstitucion).HasMaxLength(255);
            entity.Property(e => e.tipo_sangre).HasMaxLength(6);
            entity.Property(e => e.tituloColegio).HasMaxLength(200);
            entity.Property(e => e.user_alumno).HasMaxLength(20);
        });

        modelBuilder.Entity<asignaciones_profesores>(entity =>
        {
            entity.HasKey(e => new { e.idProfesor, e.idAsignatura, e.idPeriodo, e.idModalidad, e.idSeccion, e.idNivel, e.paralelo })
                .HasName("PRIMARY")
                .HasAnnotation("MySql:IndexPrefixLength", new[] { 0, 0, 0, 0, 0, 0, 0 });

            entity.HasIndex(e => e.idAsignacion, "idAsignacion").IsUnique();

            entity.Property(e => e.idProfesor).HasMaxLength(14);
            entity.Property(e => e.idAsignatura).HasColumnType("int(11)");
            entity.Property(e => e.idPeriodo).HasMaxLength(7);
            entity.Property(e => e.idModalidad).HasColumnType("int(11)");
            entity.Property(e => e.idSeccion).HasColumnType("int(11)");
            entity.Property(e => e.idNivel).HasColumnType("int(11)");
            entity.Property(e => e.paralelo)
                .HasMaxLength(1)
                .IsFixedLength();
            entity.Property(e => e.activo)
                .HasDefaultValueSql("'1'")
                .HasColumnType("tinyint(4)");
            entity.Property(e => e.codigo_asignacion).HasMaxLength(10);
            entity.Property(e => e.contabilizarHoraDocente)
                .HasDefaultValueSql("'1'")
                .HasColumnType("tinyint(4)");
            entity.Property(e => e.entrega_acta)
                .HasDefaultValueSql("'0'")
                .HasColumnType("tinyint(4)");
            entity.Property(e => e.esActivaAsignacion)
                .HasDefaultValueSql("'1'")
                .HasColumnType("tinyint(4)");
            entity.Property(e => e.extraCurricular).HasColumnType("tinyint(4)");
            entity.Property(e => e.fecha_grabar).HasColumnType("datetime");
            entity.Property(e => e.fecha_modificacion).HasColumnType("datetime");
            entity.Property(e => e.horasPracticoExperimental)
                .HasPrecision(10, 2)
                .HasDefaultValueSql("'0.00'");
            entity.Property(e => e.idAsignacion)
                .ValueGeneratedOnAdd()
                .HasColumnType("int(11)");
            entity.Property(e => e.ingresa_notas)
                .HasDefaultValueSql("'0'")
                .HasColumnType("tinyint(4)");
            entity.Property(e => e.numeroHoras).HasPrecision(10, 2);
            entity.Property(e => e.user_acta).HasMaxLength(25);
            entity.Property(e => e.user_asignaciones).HasMaxLength(25);
        });

        modelBuilder.Entity<asignaturas>(entity =>
        {
            entity.HasKey(e => e.idAsignatura).HasName("PRIMARY");

            entity.Property(e => e.idAsignatura).HasColumnType("int(11)");
            entity.Property(e => e.asignatura).HasMaxLength(200);
            entity.Property(e => e.codigo).HasMaxLength(30);
            entity.Property(e => e.extraCurricular).HasColumnType("tinyint(4)");
        });

        modelBuilder.Entity<carreras>(entity =>
        {
            entity.HasKey(e => e.idCarrera).HasName("PRIMARY");

            entity.Property(e => e.idCarrera).HasColumnType("int(11)");
            entity.Property(e => e.Carrera).HasMaxLength(100);
            entity.Property(e => e.aliasCarrera).HasMaxLength(5);
            entity.Property(e => e.codigo_cases).HasMaxLength(20);
            entity.Property(e => e.directorCarrera).HasMaxLength(100);
            entity.Property(e => e.esInstituto)
                .HasDefaultValueSql("'0'")
                .HasColumnType("tinyint(4)");
            entity.Property(e => e.numero_alumnos).HasColumnType("int(11)");
            entity.Property(e => e.numero_creditos).HasColumnType("int(11)");
            entity.Property(e => e.ordenCarrera)
                .HasDefaultValueSql("'0'")
                .HasColumnType("int(11)");
            entity.Property(e => e.revisaArrastres)
                .HasDefaultValueSql("'1'")
                .HasColumnType("tinyint(4)");
        });

        modelBuilder.Entity<cplec_asistencias>(entity =>
        {
            entity.HasKey(e => e.idAsistencia).HasName("PRIMARY");

            entity
                .ToTable(tb => tb.HasComment("cplec — Asistencia por estudiante y sesion"))
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.HasIndex(e => new { e.idMatricula, e.estado }, "ix_cplec_asistencias_matricula_estado");

            entity.HasIndex(e => new { e.idSesion, e.idMatricula }, "uq_cplec_asistencias_sesion_matricula").IsUnique();

            entity.Property(e => e.idAsistencia).HasColumnType("int(11)");
            entity.Property(e => e.estado)
                .HasDefaultValueSql("'presente'")
                .HasColumnType("enum('presente','ausente','atraso','justificado')");
            entity.Property(e => e.fechaActualizacion)
                .ValueGeneratedOnAddOrUpdate()
                .HasColumnType("datetime");
            entity.Property(e => e.fechaCreacion)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("datetime");
            entity.Property(e => e.idMatricula)
                .HasComment("FK matriculas.idMatricula")
                .HasColumnType("int(11)");
            entity.Property(e => e.idSesion)
                .HasComment("FK cplec_sesiones.idSesion")
                .HasColumnType("int(11)");
            entity.Property(e => e.minutosAtraso)
                .HasComment("Solo aplica cuando estado = atraso")
                .HasColumnType("smallint(5) unsigned");
            entity.Property(e => e.observacion).HasMaxLength(200);
            entity.Property(e => e.usuarioActualiza).HasMaxLength(25);
            entity.Property(e => e.usuarioCreacion)
                .HasMaxLength(25)
                .HasComment("usuarios.idSigafi del autor");

            entity.HasOne(d => d.idMatriculaNavigation).WithMany(p => p.cplec_asistencias)
                .HasForeignKey(d => d.idMatricula)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_cplec_asistencias_matricula");

            entity.HasOne(d => d.idSesionNavigation).WithMany(p => p.cplec_asistencias)
                .HasForeignKey(d => d.idSesion)
                .HasConstraintName("fk_cplec_asistencias_sesion");
        });

        modelBuilder.Entity<cplec_asistencias_historial>(entity =>
        {
            entity.HasKey(e => e.idHistorial).HasName("PRIMARY");

            entity
                .ToTable(tb => tb.HasComment("cplec — Auditoria de cambios de asistencia"))
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.HasIndex(e => e.idAsistencia, "ix_cplec_hist_asistencia");

            entity.HasIndex(e => e.fecha, "ix_cplec_hist_fecha");

            entity.HasIndex(e => e.idSesion, "ix_cplec_hist_sesion");

            entity.Property(e => e.idHistorial).HasColumnType("bigint(20) unsigned");
            entity.Property(e => e.estadoAnterior)
                .HasMaxLength(15)
                .HasComment("NULL = alta inicial");
            entity.Property(e => e.estadoNuevo).HasMaxLength(15);
            entity.Property(e => e.fecha)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("datetime");
            entity.Property(e => e.idAsistencia).HasColumnType("int(11)");
            entity.Property(e => e.idMatricula)
                .HasComment("Desnormalizado por el mismo motivo")
                .HasColumnType("int(11)");
            entity.Property(e => e.idSesion)
                .HasComment("Desnormalizado: sobrevive al borrado en cascada")
                .HasColumnType("int(11)");
            entity.Property(e => e.ipAddress).HasMaxLength(45);
            entity.Property(e => e.motivo).HasMaxLength(250);
            entity.Property(e => e.rol)
                .HasMaxLength(25)
                .HasComment("cplec_docente | cplec_inspector");
            entity.Property(e => e.usuario)
                .HasMaxLength(25)
                .HasComment("usuarios.idSigafi tomado del JWT");
        });

        modelBuilder.Entity<cplec_sesiones>(entity =>
        {
            entity.HasKey(e => e.idSesion).HasName("PRIMARY");

            entity
                .ToTable(tb => tb.HasComment("cplec — Sesion de clase del leccionario (grano: dia)"))
                .HasCharSet("utf8mb4")
                .UseCollation("utf8mb4_unicode_ci");

            entity.HasIndex(e => new { e.idAsignacion, e.activo }, "ix_cplec_sesiones_asignacion");

            entity.HasIndex(e => new { e.estado, e.idFecha }, "ix_cplec_sesiones_estado_fecha");

            entity.HasIndex(e => e.idFecha, "ix_cplec_sesiones_fecha");

            entity.HasIndex(e => new { e.idAsignacion, e.idFecha, e.numeroBloque }, "uq_cplec_sesiones_asignacion_fecha_bloque").IsUnique();

            entity.Property(e => e.idSesion).HasColumnType("int(11)");
            entity.Property(e => e.activo)
                .IsRequired()
                .HasDefaultValueSql("'1'");
            entity.Property(e => e.estado)
                .HasDefaultValueSql("'borrador'")
                .HasComment("cerrada = congelada; solo un inspector puede reabrirla")
                .HasColumnType("enum('borrador','cerrada')");
            entity.Property(e => e.fechaActualizacion)
                .ValueGeneratedOnAddOrUpdate()
                .HasColumnType("datetime");
            entity.Property(e => e.fechaCierre).HasColumnType("datetime");
            entity.Property(e => e.fechaCreacion)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("datetime");
            entity.Property(e => e.idAsignacion)
                .HasComment("FK asignaciones_profesores.idAsignacion (columna UNIQUE, no la PK compuesta)")
                .HasColumnType("int(11)");
            entity.Property(e => e.idFecha)
                .HasComment("FK fechas_horarios.idFecha — el dia de la clase")
                .HasColumnType("int(11)");
            entity.Property(e => e.numeroBloque)
                .HasDefaultValueSql("'1'")
                .HasComment("Orden de la clase dentro del dia. Normalmente 1.")
                .HasColumnType("tinyint(4)");
            entity.Property(e => e.observacion)
                .HasMaxLength(500)
                .HasComment("Observaciones generales de la sesion");
            entity.Property(e => e.tema)
                .HasMaxLength(250)
                .HasComment("Tema general de la clase — el leccionario propiamente dicho");
            entity.Property(e => e.usuarioActualiza).HasMaxLength(25);
            entity.Property(e => e.usuarioCreacion)
                .HasMaxLength(25)
                .HasComment("usuarios.idSigafi del docente");

            entity.HasOne(d => d.idAsignacionNavigation).WithMany(p => p.cplec_sesiones)
                .HasPrincipalKey(p => p.idAsignacion)
                .HasForeignKey(d => d.idAsignacion)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_cplec_sesiones_asignacion");

            entity.HasOne(d => d.idFechaNavigation).WithMany(p => p.cplec_sesiones)
                .HasForeignKey(d => d.idFecha)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_cplec_sesiones_fecha");
        });

        modelBuilder.Entity<cursos>(entity =>
        {
            entity.HasKey(e => e.idNivel).HasName("PRIMARY");

            entity.HasIndex(e => e.idCarrera, "R_5");

            entity.Property(e => e.idNivel).HasColumnType("int(11)");
            entity.Property(e => e.Nivel).HasMaxLength(20);
            entity.Property(e => e.aliasCurso).HasMaxLength(5);
            entity.Property(e => e.esRecuperacion)
                .HasDefaultValueSql("'0'")
                .HasColumnType("tinyint(4)");
            entity.Property(e => e.idCarrera).HasColumnType("int(11)");
            entity.Property(e => e.jerarquia).HasColumnType("int(11)");
            entity.Property(e => e.orden).HasColumnType("int(11)");

            entity.HasOne(d => d.idCarreraNavigation).WithMany(p => p.cursos)
                .HasForeignKey(d => d.idCarrera)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("cursos_ibfk_1");
        });

        modelBuilder.Entity<espacios>(entity =>
        {
            entity.HasKey(e => e.idEspacio).HasName("PRIMARY");

            entity.Property(e => e.idEspacio).HasColumnType("int(11)");
            entity.Property(e => e.activo).HasColumnType("tinyint(4)");
            entity.Property(e => e.espacio).HasMaxLength(100);
        });

        modelBuilder.Entity<fechas_horarios>(entity =>
        {
            entity.HasKey(e => e.idFecha).HasName("PRIMARY");

            entity.HasIndex(e => e.fecha, "ix_fechas_horarios_fecha");

            entity.Property(e => e.idFecha).HasColumnType("int(11)");
            entity.Property(e => e.dia).HasMaxLength(15);
            entity.Property(e => e.finsemana)
                .HasDefaultValueSql("'0'")
                .HasColumnType("tinyint(4)");
        });

        modelBuilder.Entity<horario_detalle>(entity =>
        {
            entity.HasKey(e => e.idHorario).HasName("PRIMARY");

            entity.HasIndex(e => new { e.idFecha, e.idhora, e.activo }, "ix_horario_detalle_fecha_hora_activo");

            entity.Property(e => e.idHorario).HasColumnType("int(11)");
            entity.Property(e => e.activo).HasColumnType("tinyint(4)");
            entity.Property(e => e.claseReasignacion).HasColumnType("tinyint(4)");
            entity.Property(e => e.esRecuperacionPedagocia).HasColumnType("tinyint(1)");
            entity.Property(e => e.idAsignacion).HasColumnType("int(11)");
            entity.Property(e => e.idEspacio).HasColumnType("int(11)");
            entity.Property(e => e.idFecha).HasColumnType("int(11)");
            entity.Property(e => e.idHorarioReasgincacion).HasColumnType("int(11)");
            entity.Property(e => e.idhora).HasColumnType("int(11)");
            entity.Property(e => e.observacion).HasMaxLength(255);
            entity.Property(e => e.tipoBloque).HasMaxLength(50);

            entity.HasOne(d => d.idEspacioNavigation).WithMany(p => p.horario_detalle)
                .HasForeignKey(d => d.idEspacio)
                .HasConstraintName("fk_horario_detalle_espacios1");
        });

        modelBuilder.Entity<horas_clases>(entity =>
        {
            entity.HasKey(e => e.idhora).HasName("PRIMARY");

            entity.Property(e => e.idhora).HasColumnType("int(11)");
            entity.Property(e => e.activo).HasColumnType("tinyint(4)");
            entity.Property(e => e.hora_fin).HasMaxLength(8);
            entity.Property(e => e.hora_inicio).HasMaxLength(8);
            entity.Property(e => e.idCarrera).HasColumnType("int(11)");
            entity.Property(e => e.idSeccion).HasColumnType("int(11)");
            entity.Property(e => e.minutos).HasColumnType("int(11)");
            entity.Property(e => e.numero_hora).HasColumnType("int(11)");
            entity.Property(e => e.tipo).HasMaxLength(1);
        });

        modelBuilder.Entity<gest_audit_registros>(entity =>
        {
            entity.HasKey(e => e.idAuditRegistros).HasName("PRIMARY");

            entity.HasIndex(e => new { e.accion, e.fechaHora }, "ix_audit_accion_fecha");

            entity.HasIndex(e => new { e.codigoSistema, e.idAuditRegistros }, "ix_audit_codigo_sistema");

            entity.HasIndex(e => new { e.idEntidad, e.tablaAfectada }, "ix_audit_entidad");

            entity.HasIndex(e => e.jti, "ix_audit_jti");

            entity.HasIndex(e => new { e.codigoSistema, e.idModulo, e.fechaHora }, "ix_audit_sistema_modulo_fecha");

            entity.HasIndex(e => new { e.idUsuario, e.codigoSistema, e.fechaHora }, "ix_audit_sistema_usuario_fecha");

            entity.Property(e => e.idAuditRegistros).HasColumnType("bigint(20)");
            entity.Property(e => e.accion).HasMaxLength(100);
            entity.Property(e => e.codigoSistema).HasMaxLength(20);
            entity.Property(e => e.datosAnteriores).HasColumnType("text");
            entity.Property(e => e.datosNuevos).HasColumnType("text");
            entity.Property(e => e.descripcion).HasColumnType("text");
            entity.Property(e => e.duracionMs).HasColumnType("int(11)");
            entity.Property(e => e.fechaHora).HasColumnType("datetime");
            entity.Property(e => e.idEntidad).HasColumnType("int(11)");
            entity.Property(e => e.idModulo).HasMaxLength(50);
            entity.Property(e => e.idUsuario).HasMaxLength(14);
            entity.Property(e => e.ipOrigen).HasMaxLength(45);
            entity.Property(e => e.jti).HasMaxLength(50);
            entity.Property(e => e.mensajeError).HasColumnType("text");
            entity.Property(e => e.requestMethod).HasMaxLength(10);
            entity.Property(e => e.requestPath).HasMaxLength(500);
            entity.Property(e => e.rol).HasMaxLength(30);
            entity.Property(e => e.statusCode).HasColumnType("int(11)");
            entity.Property(e => e.tablaAfectada).HasMaxLength(100);
            entity.Property(e => e.userAgent).HasMaxLength(500);
        });

        modelBuilder.Entity<matriculas>(entity =>
        {
            entity.HasKey(e => e.idMatricula).HasName("PRIMARY");

            entity.HasIndex(e => e.idAlumno, "R_3");

            entity.HasIndex(e => e.idSeccion, "R_4");

            entity.HasIndex(e => e.idNivel, "R_6");

            entity.HasIndex(e => e.idModalidad, "R_7");

            entity.HasIndex(e => e.idPeriodo, "R_8");

            entity.Property(e => e.idMatricula).HasColumnType("int(11)");
            entity.Property(e => e.beca_colegiatura).HasPrecision(5, 2);
            entity.Property(e => e.beca_matricula).HasPrecision(5, 2);
            entity.Property(e => e.carrera_convalidada).HasMaxLength(200);
            entity.Property(e => e.documentoFactura).HasMaxLength(14);
            entity.Property(e => e.esOyente)
                .HasDefaultValueSql("'0'")
                .HasColumnType("tinyint(4)");
            entity.Property(e => e.fechaMatricula)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp");
            entity.Property(e => e.folio).HasColumnType("int(11)");
            entity.Property(e => e.idAlumno).HasMaxLength(14);
            entity.Property(e => e.idModalidad).HasColumnType("int(11)");
            entity.Property(e => e.idNivel).HasColumnType("int(11)");
            entity.Property(e => e.idPeriodo)
                .HasMaxLength(7)
                .IsFixedLength();
            entity.Property(e => e.idSeccion).HasColumnType("int(11)");
            entity.Property(e => e.numero_permiso).HasColumnType("int(11)");
            entity.Property(e => e.observacion).HasMaxLength(100);
            entity.Property(e => e.paralelo).HasMaxLength(10);
            entity.Property(e => e.user_matricula).HasMaxLength(20);
            entity.Property(e => e.valida)
                .HasDefaultValueSql("'1'")
                .HasColumnType("tinyint(4)");

            entity.HasOne(d => d.idAlumnoNavigation).WithMany(p => p.matriculas)
                .HasForeignKey(d => d.idAlumno)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("matriculas_ibfk_1");

            entity.HasOne(d => d.idModalidadNavigation).WithMany(p => p.matriculas)
                .HasForeignKey(d => d.idModalidad)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("matriculas_ibfk_4");

            entity.HasOne(d => d.idNivelNavigation).WithMany(p => p.matriculas)
                .HasForeignKey(d => d.idNivel)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("matriculas_ibfk_3");

            entity.HasOne(d => d.idPeriodoNavigation).WithMany(p => p.matriculas)
                .HasForeignKey(d => d.idPeriodo)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("matriculas_ibfk_5");

            entity.HasOne(d => d.idSeccionNavigation).WithMany(p => p.matriculas)
                .HasForeignKey(d => d.idSeccion)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("matriculas_ibfk_2");
        });

        modelBuilder.Entity<matriculas_asistencias>(entity =>
        {
            entity.HasKey(e => new { e.idMatricula, e.idFecha })
                .HasName("PRIMARY")
                .HasAnnotation("MySql:IndexPrefixLength", new[] { 0, 0 });

            entity.Property(e => e.idMatricula).HasColumnType("int(11)");
            entity.Property(e => e.idFecha).HasColumnType("int(11)");
            entity.Property(e => e.atraso).HasDefaultValueSql("'0'");
            entity.Property(e => e.fecha_actualizacion)
                .HasDefaultValueSql("'0000-00-00 00:00:00'")
                .HasColumnType("timestamp");
            entity.Property(e => e.fecha_creacion)
                .HasDefaultValueSql("'0000-00-00 00:00:00'")
                .HasColumnType("timestamp");
            entity.Property(e => e.noAsiste).HasDefaultValueSql("'0'");
            entity.Property(e => e.observacion).HasMaxLength(100);
            entity.Property(e => e.usuario).HasMaxLength(20);
            entity.Property(e => e.usuario_actualiza).HasMaxLength(20);
        });

        modelBuilder.Entity<modalidades>(entity =>
        {
            entity.HasKey(e => e.idModalidad).HasName("PRIMARY");

            entity.Property(e => e.idModalidad).HasColumnType("int(11)");
            entity.Property(e => e.modalidad).HasMaxLength(100);
            entity.Property(e => e.modalidadImpresion).HasMaxLength(30);
            entity.Property(e => e.sufijo)
                .HasMaxLength(1)
                .IsFixedLength();
        });

        modelBuilder.Entity<periodos>(entity =>
        {
            entity.HasKey(e => e.idPeriodo).HasName("PRIMARY");

            entity.Property(e => e.idPeriodo)
                .HasMaxLength(7)
                .HasDefaultValueSql("''")
                .IsFixedLength();
            entity.Property(e => e.detalle).HasMaxLength(100);
            entity.Property(e => e.esConduccion)
                .HasDefaultValueSql("'0'")
                .HasColumnType("tinyint(4)");
            entity.Property(e => e.esInstituto)
                .HasDefaultValueSql("'0'")
                .HasColumnType("tinyint(4)");
            entity.Property(e => e.foliop).HasColumnType("int(11)");
            entity.Property(e => e.ingresoCalificaciones)
                .HasDefaultValueSql("'0'")
                .HasColumnType("tinyint(4)");
            entity.Property(e => e.numero_pagos).HasColumnType("int(10) unsigned");
            entity.Property(e => e.periodoPlanificacion)
                .HasDefaultValueSql("'0'")
                .HasColumnType("tinyint(4)");
            entity.Property(e => e.periodoactivoinstituto)
                .HasDefaultValueSql("'0'")
                .HasColumnType("tinyint(4)");
            entity.Property(e => e.permiteCalificacionesInstituto)
                .HasDefaultValueSql("'0'")
                .HasColumnType("tinyint(4)");
            entity.Property(e => e.permiteMatricula)
                .HasDefaultValueSql("'0'")
                .HasColumnType("tinyint(4)");
            entity.Property(e => e.visualizaPowerBi)
                .HasDefaultValueSql("'0'")
                .HasColumnType("tinyint(4)");
        });

        modelBuilder.Entity<profesores>(entity =>
        {
            entity.HasKey(e => e.idProfesor).HasName("PRIMARY");

            entity.HasIndex(e => e.idDiscapacidad, "fk_profesores_discapacidades1_idx");

            entity.HasIndex(e => e.estadoCivil, "fk_profesores_estadoCivil1_idx");

            entity.HasIndex(e => e.idEtnia, "fk_profesores_etnias1_idx");

            entity.HasIndex(e => e.idNacionalidad, "fk_profesores_nacionalidades1_idx");

            entity.HasIndex(e => e.idParroquiaNacimiento, "fk_profesores_parroquias1_idx");

            entity.HasIndex(e => e.idParroquiaResidencia, "fk_profesores_parroquias2_idx");

            entity.HasIndex(e => e.tipoSangre, "fk_profesores_tipoSangre1_idx");

            entity.Property(e => e.idProfesor).HasMaxLength(14);
            entity.Property(e => e.abreviatura).HasMaxLength(5);
            entity.Property(e => e.abreviatura_post).HasMaxLength(5);
            entity.Property(e => e.activo).HasColumnType("tinyint(4)");
            entity.Property(e => e.apellidos).HasMaxLength(60);
            entity.Property(e => e.callePrincipal).HasMaxLength(125);
            entity.Property(e => e.calleSecundaria).HasMaxLength(125);
            entity.Property(e => e.celular).HasMaxLength(20);
            entity.Property(e => e.clave)
                .HasMaxLength(20)
                .HasDefaultValueSql("'321'");
            entity.Property(e => e.codigoPostal).HasMaxLength(20);
            entity.Property(e => e.direccion).HasMaxLength(100);
            entity.Property(e => e.email).HasMaxLength(100);
            entity.Property(e => e.emailInstitucional).HasMaxLength(255);
            entity.Property(e => e.esReal)
                .HasDefaultValueSql("'1'")
                .HasColumnType("tinyint(4)");
            entity.Property(e => e.estadoCivil).HasColumnType("int(11)");
            entity.Property(e => e.foto).HasMaxLength(255);
            entity.Property(e => e.idDiscapacidad).HasColumnType("int(11)");
            entity.Property(e => e.idEtnia).HasColumnType("int(11)");
            entity.Property(e => e.idNacionalidad).HasColumnType("int(11)");
            entity.Property(e => e.idParroquiaNacimiento).HasColumnType("int(11)");
            entity.Property(e => e.idParroquiaResidencia).HasColumnType("int(11)");
            entity.Property(e => e.nacionalidad).HasMaxLength(40);
            entity.Property(e => e.nombres).HasMaxLength(60);
            entity.Property(e => e.numeroCasa).HasMaxLength(45);
            entity.Property(e => e.numeroConadis).HasMaxLength(45);
            entity.Property(e => e.porcentajeDiscapacidad).HasColumnType("int(11)");
            entity.Property(e => e.practicas)
                .HasDefaultValueSql("'0'")
                .HasColumnType("tinyint(4)");
            entity.Property(e => e.primerApellido).HasMaxLength(60);
            entity.Property(e => e.primerNombre).HasMaxLength(60);
            entity.Property(e => e.segundoApellido).HasMaxLength(60);
            entity.Property(e => e.segundoNombre).HasMaxLength(60);
            entity.Property(e => e.sexo)
                .HasMaxLength(1)
                .IsFixedLength();
            entity.Property(e => e.telefono).HasMaxLength(30);
            entity.Property(e => e.tipo)
                .HasMaxLength(1)
                .HasDefaultValueSql("'P'")
                .IsFixedLength();
            entity.Property(e => e.tipoSangre).HasMaxLength(5);
            entity.Property(e => e.tipodocumento)
                .HasMaxLength(1)
                .IsFixedLength();
            entity.Property(e => e.titulo).HasMaxLength(200);
        });

        modelBuilder.Entity<rbac_modulos>(entity =>
        {
            entity.HasKey(e => e.idModulos).HasName("PRIMARY");

            entity.HasIndex(e => e.id_sistema, "fk_modulos_sistema1_idx");

            entity.Property(e => e.idModulos).HasColumnType("int(11)");
            entity.Property(e => e.Nombre).HasMaxLength(255);
            entity.Property(e => e.esActivo).HasColumnType("tinyint(4)");
            entity.Property(e => e.id_sistema).HasColumnType("int(11)");

            entity.HasOne(d => d.id_sistemaNavigation).WithMany(p => p.rbac_modulos)
                .HasForeignKey(d => d.id_sistema)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_modulos_sistema1");
        });

        modelBuilder.Entity<rbac_modulos_operaciones>(entity =>
        {
            entity.HasKey(e => e.idModulosOperaciones).HasName("PRIMARY");

            entity.HasIndex(e => e.idModulos, "fk_modulos_operaciones_modulos1_idx");

            entity.HasIndex(e => e.idOperaciones, "fk_modulos_operaciones_operaciones1_idx");

            entity.Property(e => e.idModulosOperaciones).HasColumnType("int(11)");
            entity.Property(e => e.esActivo).HasColumnType("tinyint(4)");
            entity.Property(e => e.idModulos).HasColumnType("int(11)");
            entity.Property(e => e.idOperaciones).HasColumnType("int(11)");

            entity.HasOne(d => d.idModulosNavigation).WithMany(p => p.rbac_modulos_operaciones)
                .HasForeignKey(d => d.idModulos)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_modulos_operaciones_modulos1");

            entity.HasOne(d => d.idOperacionesNavigation).WithMany(p => p.rbac_modulos_operaciones)
                .HasForeignKey(d => d.idOperaciones)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_modulos_operaciones_operaciones1");
        });

        modelBuilder.Entity<rbac_operaciones>(entity =>
        {
            entity.HasKey(e => e.idOperaciones).HasName("PRIMARY");

            entity.Property(e => e.idOperaciones).HasColumnType("int(11)");
            entity.Property(e => e.NombreOperacion).HasMaxLength(100);
        });

        modelBuilder.Entity<rbac_refresh_tokens>(entity =>
        {
            entity.HasKey(e => e.idRefreshToken).HasName("PRIMARY");

            entity.HasIndex(e => new { e.idUsuario, e.revokedAt }, "rbac_refresh_tokens_idUsuario_IDX");

            entity.HasIndex(e => e.tokenHash, "rbac_refresh_tokens_tokenHash_IDX").IsUnique();

            entity.Property(e => e.idRefreshToken).HasColumnType("bigint(20) unsigned");
            entity.Property(e => e.createdAt).HasColumnType("datetime");
            entity.Property(e => e.deviceInfo).HasMaxLength(255);
            entity.Property(e => e.expiresAt).HasColumnType("datetime");
            entity.Property(e => e.familyId).HasMaxLength(36);
            entity.Property(e => e.idUsuario).HasColumnType("int(11)");
            entity.Property(e => e.ipAddress).HasMaxLength(45);
            entity.Property(e => e.replacedByTokenId).HasColumnType("bigint(20) unsigned");
            entity.Property(e => e.revokedAt).HasColumnType("datetime");
            entity.Property(e => e.revokedReason).HasMaxLength(30);
            entity.Property(e => e.sequence).HasColumnType("int(10) unsigned");

            entity.HasOne(d => d.idUsuarioNavigation).WithMany(p => p.rbac_refresh_tokens)
                .HasForeignKey(d => d.idUsuario)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("rbac_refresh_tokens_usuarios_FK");
        });

        modelBuilder.Entity<rbac_rol>(entity =>
        {
            entity.HasKey(e => e.idRol).HasName("PRIMARY");

            entity.HasIndex(e => e.codigo_rol, "codigo_rol_UNIQUE").IsUnique();

            entity.Property(e => e.idRol).HasColumnType("int(11)");
            entity.Property(e => e.Nombre).HasMaxLength(255);
            entity.Property(e => e.codigo_rol).HasMaxLength(25);
            entity.Property(e => e.esActivo).HasColumnType("tinyint(4)");
        });

        modelBuilder.Entity<rbac_rol_modulo_operacion>(entity =>
        {
            entity.HasKey(e => e.idRolModuloOperacion).HasName("PRIMARY");

            entity.HasIndex(e => e.idModulosOperaciones, "fk_rol_modulo_operacion_modulos_operaciones1_idx");

            entity.HasIndex(e => e.idRol, "fk_rol_modulo_operacion_rol1_idx");

            entity.Property(e => e.idRolModuloOperacion).HasColumnType("int(11)");
            entity.Property(e => e.esActivo).HasColumnType("tinyint(4)");
            entity.Property(e => e.idModulosOperaciones).HasColumnType("int(11)");
            entity.Property(e => e.idRol).HasColumnType("int(11)");

            entity.HasOne(d => d.idModulosOperacionesNavigation).WithMany(p => p.rbac_rol_modulo_operacion)
                .HasForeignKey(d => d.idModulosOperaciones)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_rol_modulo_operacion_modulos_operaciones1");

            entity.HasOne(d => d.idRolNavigation).WithMany(p => p.rbac_rol_modulo_operacion)
                .HasForeignKey(d => d.idRol)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_rol_modulo_operacion_rol1");
        });

        modelBuilder.Entity<rbac_sistema>(entity =>
        {
            entity.HasKey(e => e.idSistema).HasName("PRIMARY");

            entity.HasIndex(e => e.codigo, "rbac_sistema_codigo_IDX").IsUnique();

            entity.Property(e => e.idSistema).HasColumnType("int(11)");
            entity.Property(e => e.codigo).HasMaxLength(20);
            entity.Property(e => e.detalle).HasMaxLength(50);
            entity.Property(e => e.icono).HasMaxLength(50);
            entity.Property(e => e.url).HasMaxLength(500);
        });

        modelBuilder.Entity<rbac_usuario_rol>(entity =>
        {
            entity.HasKey(e => e.idUsuarioRol).HasName("PRIMARY");

            entity.HasIndex(e => e.idRol, "fk_usuario_rol_rol1_idx");

            entity.HasIndex(e => e.idUsuario, "fk_usuario_rol_usuarios1_idx");

            entity.Property(e => e.idUsuarioRol).HasColumnType("int(11)");
            entity.Property(e => e.esActivo).HasColumnType("tinyint(4)");
            entity.Property(e => e.idRol).HasColumnType("int(11)");
            entity.Property(e => e.idUsuario).HasColumnType("int(11)");

            entity.HasOne(d => d.idRolNavigation).WithMany(p => p.rbac_usuario_rol)
                .HasForeignKey(d => d.idRol)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_usuario_rol_rol1");

            entity.HasOne(d => d.idUsuarioNavigation).WithMany(p => p.rbac_usuario_rol)
                .HasForeignKey(d => d.idUsuario)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_usuario_rol_usuarios1");
        });

        modelBuilder.Entity<secciones>(entity =>
        {
            entity.HasKey(e => e.idSeccion).HasName("PRIMARY");

            entity.Property(e => e.idSeccion).HasColumnType("int(11)");
            entity.Property(e => e.seccion).HasMaxLength(30);
            entity.Property(e => e.sufijo)
                .HasMaxLength(1)
                .IsFixedLength();
        });

        modelBuilder.Entity<usuarios>(entity =>
        {
            entity.HasKey(e => e.idUsuario).HasName("PRIMARY");

            entity.HasIndex(e => e.idSigafi, "usuario_UNIQUE").IsUnique();

            entity.Property(e => e.idUsuario).HasColumnType("int(11)");
            entity.Property(e => e.activo)
                .HasDefaultValueSql("'1'")
                .HasColumnType("tinyint(4)");
            entity.Property(e => e.administrador).HasColumnType("tinyint(4)");
            entity.Property(e => e.contrasenia).HasMaxLength(250);
            entity.Property(e => e.emailInstitucional).HasMaxLength(100);
            entity.Property(e => e.emailValidado).HasColumnType("tinyint(4)");
            entity.Property(e => e.fechaEmailValidacion).HasColumnType("datetime");
            entity.Property(e => e.hashEmailToken).HasMaxLength(255);
            entity.Property(e => e.idSigafi)
                .HasMaxLength(20)
                .HasComment("este es idSifafi\\n");
            entity.Property(e => e.nombre).HasMaxLength(200);
            entity.Property(e => e.tablaSigafi).HasColumnType("enum('alumno','profesor','otros')");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
