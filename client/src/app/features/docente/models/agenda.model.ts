export type EstadoBloque = 'Pendiente' | 'Borrador' | 'Cerrada' | 'Futura';

export interface BloqueAgendaDto {
  fecha: string;
  dia: string;
  idHorarioInicio: number;
  numeroBloque: number;
  horaInicio: string;
  horaFin: string;
  franjasPlanificadas: number;
  minutosPlanificados: number;
  estado: EstadoBloque;
  idSesion: number | null;
  diasRetraso: number;
}
