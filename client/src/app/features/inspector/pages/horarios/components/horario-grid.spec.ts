import type { ComponentFixture} from '@angular/core/testing';
import { TestBed } from '@angular/core/testing';
import { describe, it, expect, beforeEach, vi } from 'vitest';
import type { ComponentRef } from '@angular/core';

import { HorarioGridComponent } from './horario-grid';
import type { DiaGridDto, FranjaDto, CeldaGridDto } from '../../../models/horario.model';

describe('HorarioGridComponent', () => {
  let fixture: ComponentFixture<HorarioGridComponent>;
  let component: HorarioGridComponent;
  let componentRef: ComponentRef<HorarioGridComponent>;

  const dia1: DiaGridDto = { dia: 'Lunes', fecha: '2026-08-10', idFecha: 1, habilitado: true, motivo: null };
  const franja1: FranjaDto = { idhora: 10, horaInicio: '08:00', horaFin: '09:00' };
  const celda1: CeldaGridDto = {
    idHorario: 100,
    idAsignacion: 5,
    idhora: 10,
    dia: 'Lunes',
    nombreDocente: 'Gomez Ana',
    tipoBloque: null,
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HorarioGridComponent],
    });
    fixture = TestBed.createComponent(HorarioGridComponent);
    component = fixture.componentInstance;
    componentRef = fixture.componentRef;
  });

  it('renderiza clases de conflicto bloqueante (rojo) y advertencia (ámbar)', () => {
    componentRef.setInput('dias', [dia1]);
    componentRef.setInput('franjas', [franja1]);
    componentRef.setInput('celdas', [celda1]);
    componentRef.setInput('celdaConflictos', { 100: 'bloqueante' });
    fixture.detectChanges();

    const cellEl = fixture.nativeElement.querySelector('td.cell-slot');
    expect(cellEl.classList.contains('conflicto-bloqueante')).toBe(true);

    componentRef.setInput('celdaConflictos', { 100: 'advertencia' });
    fixture.detectChanges();

    expect(cellEl.classList.contains('conflicto-advertencia')).toBe(true);
  });

  it('modo lectura no emite celdaClick', () => {
    componentRef.setInput('dias', [dia1]);
    componentRef.setInput('franjas', [franja1]);
    componentRef.setInput('celdas', [celda1]);
    componentRef.setInput('modo', 'lectura');
    fixture.detectChanges();

    const spyEmitted = vi.fn();
    component.celdaClick.subscribe(spyEmitted);

    const cellEl: HTMLElement = fixture.nativeElement.querySelector('td.cell-slot');
    cellEl.click();

    expect(spyEmitted).not.toHaveBeenCalled();
  });
});
