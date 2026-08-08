export type EstadoBloqueHorario = 'Pendiente' | 'Borrador' | 'Cerrada' | 'Futura';

export interface BloqueMiHorario {
  idAsignacion: number;
  tipoLicencia: string;
  jornada: string;
  paralelo: string;
  asignatura: string;
  fecha: string;
  dia: string;
  idHorarioInicio: number;
  numeroBloque: number;
  horaInicio: string;
  horaFin: string;
  franjasPlanificadas: number;
  minutosPlanificados: number;
  estado: EstadoBloqueHorario;
  idSesion: number | null;
  diasRetraso: number;
}
