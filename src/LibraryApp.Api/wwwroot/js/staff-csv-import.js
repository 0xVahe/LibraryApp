(() => {
    document.querySelectorAll('[data-staff-csv-import]').forEach(form => {
        const button = form.querySelector('button[type="submit"]');
        const label = form.querySelector('[data-import-label]');
        const spinner = form.querySelector('.staff-import-spinner');
        const status = form.querySelector('[data-import-status]');
        const fileArea = form.querySelector('[data-import-file]');
        const template = form.querySelector('[data-import-template]');
        const recovery = form.querySelector('[data-import-recovery]');
        const idleFileInert = fileArea.inert;
        const templateAttributes = ['href', 'tabindex', 'aria-disabled'].map(name => [name, template.getAttribute(name)]);
        const idleLabel = label.textContent;
        let submitting = false;

        const blockWhileSubmitting = event => {
            if (submitting) event.preventDefault();
        };
        template.addEventListener('click', blockWhileSubmitting);
        template.addEventListener('auxclick', blockWhileSubmitting);
        template.addEventListener('keydown', event => {
            if (event.key === 'Enter' || event.key === ' ') blockWhileSubmitting(event);
        });
        // Also prevent dropping a different file while the upload area is inert.
        form.addEventListener('dragover', blockWhileSubmitting);
        form.addEventListener('drop', blockWhileSubmitting);

        form.addEventListener('submit', event => {
            if (submitting) {
                event.preventDefault();
                return;
            }
            if (event.defaultPrevented) return;

            submitting = true;
            // Leave the file and antiforgery fields enabled for the normal multipart POST.
            button.disabled = true;
            label.textContent = 'Importing…';
            spinner.hidden = false;
            status.textContent = 'Please wait while the staff members are imported.';
            // Inert blocks the picker/label and keyboard interaction without excluding the file from POST data.
            fileArea.inert = true;
            template.removeAttribute('href');
            template.setAttribute('tabindex', '-1');
            template.setAttribute('aria-disabled', 'true');
            recovery.hidden = false;
        });

        // Back/Forward can restore the same document, including its disabled button.
        window.addEventListener('pageshow', () => {
            submitting = false;
            button.disabled = false;
            label.textContent = idleLabel;
            spinner.hidden = true;
            status.textContent = '';
            fileArea.inert = idleFileInert;
            templateAttributes.forEach(([name, value]) => {
                if (value === null) template.removeAttribute(name);
                else template.setAttribute(name, value);
            });
            recovery.hidden = true;
        });
    });
})();
