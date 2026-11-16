(() => {
    const form = document.querySelector('[data-negotiation-action-form]');
    if (!form) return;
    const kind = form.querySelector('#negotiation-action-kind');
    const due = form.querySelector('[data-action-due]');
    const dueInput = due?.querySelector('input');
    const appointment = form.querySelector('[data-action-appointment]');
    const appointmentInputs = appointment?.querySelectorAll('input,select,textarea');
    const update = () => {
        const isAppointment = kind.value === 'Compromisso';
        const needsDue = kind.value === 'Contato' || kind.value === 'Pendência';
        if (due) { due.hidden = isAppointment || !needsDue; due.style.display = due.hidden ? 'none' : ''; }
        if (dueInput) dueInput.required = needsDue;
        if (appointment) { appointment.hidden = !isAppointment; appointment.style.display = appointment.hidden ? 'none' : ''; }
        appointmentInputs?.forEach(input => input.required = isAppointment && input.name !== 'ActionAppointmentNotes');
    };
    kind.addEventListener('change', update);
    update();
})();
