// Run with: node tests/ui/staff-csv-import.test.cjs
// Event/attribute tests only; browser navigation and native file picking still need browser checks.
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(path.join(__dirname, '../../src/LibraryApp.Api/wwwroot/js/staff-csv-import.js'), 'utf8');

class Element extends EventTarget {
    constructor(attributes = {}) {
        super();
        this.attributes = new Map(Object.entries(attributes));
        this.hidden = false;
        this.disabled = false;
        this.inert = false;
        this.textContent = '';
    }
    getAttribute(name) { return this.attributes.get(name) ?? null; }
    setAttribute(name, value) { this.attributes.set(name, value); }
    removeAttribute(name) { this.attributes.delete(name); }
}

function setup(templateAttributes = { href: '/staff/import?handler=Template' }) {
    const form = new Element();
    const window = new EventTarget();
    const button = new Element();
    const label = new Element();
    label.textContent = 'Import';
    const spinner = new Element();
    spinner.hidden = true;
    const status = new Element();
    const fileArea = new Element();
    const template = new Element(templateAttributes);
    const recovery = new Element();
    recovery.hidden = true;
    const controls = { 'button[type="submit"]': button, '[data-import-label]': label,
        '.staff-import-spinner': spinner, '[data-import-status]': status,
        '[data-import-file]': fileArea, '[data-import-template]': template,
        '[data-import-recovery]': recovery };
    form.querySelector = selector => controls[selector];
    vm.runInNewContext(source, { document: { querySelectorAll: () => [form] }, window,
        setTimeout: () => assert.fail('Must not reset or retry on a timeout') });
    return { form, window, button, label, spinner, status, fileArea, template, recovery };
}

function fire(target, type, key) {
    const event = new Event(type, { cancelable: true });
    if (key) event.key = key;
    return target.dispatchEvent(event);
}

test('first submit continues normally, shows feedback and blocks repeats', () => {
    const ui = setup();
    assert.equal(fire(ui.form, 'submit'), true);
    assert.equal(ui.button.disabled, true);
    assert.equal(ui.label.textContent, 'Importing\u2026');
    assert.equal(ui.spinner.hidden, false);
    assert.equal(ui.status.textContent, 'Please wait while the staff members are imported.');
    for (let i = 0; i < 10; i++) assert.equal(fire(ui.form, 'submit'), false);
});

test('pending upload removes the template destination and blocks mouse and keyboard activation', () => {
    const ui = setup();
    assert.equal(fire(ui.template, 'click'), true);
    fire(ui.form, 'submit');
    assert.equal(ui.template.getAttribute('href'), null);
    assert.equal(ui.template.getAttribute('tabindex'), '-1');
    assert.equal(ui.template.getAttribute('aria-disabled'), 'true');
    assert.equal(fire(ui.template, 'click'), false);
    assert.equal(fire(ui.template, 'auxclick'), false);
    assert.equal(fire(ui.template, 'keydown', 'Enter'), false);
    assert.equal(fire(ui.template, 'keydown', ' '), false);
});

test('pending upload makes the file area inert and blocks replacement drops', () => {
    const ui = setup();
    fire(ui.form, 'submit');
    assert.equal(ui.fileArea.inert, true);
    assert.equal(ui.fileArea.disabled, false);
    assert.equal(ui.form.disabled, false);
    assert.equal(fire(ui.form, 'dragover'), false);
    assert.equal(fire(ui.form, 'drop'), false);
});

test('a stuck/canceled upload keeps its guard and exposes explicit recovery', () => {
    const ui = setup();
    assert.equal(ui.recovery.hidden, true);
    fire(ui.form, 'submit');
    // Native POST cancellation has no reliable DOM event: keep pending until navigation/recovery.
    fire(ui.window, 'focus');
    fire(ui.window, 'error');
    assert.equal(ui.recovery.hidden, false);
    assert.equal(ui.button.disabled, true);
    assert.equal(fire(ui.form, 'submit'), false);
});

test('pageshow restores all controls and allows another normal submission', () => {
    const ui = setup();
    fire(ui.form, 'submit');
    fire(ui.window, 'pageshow');
    assert.equal(ui.button.disabled, false);
    assert.equal(ui.label.textContent, 'Import');
    assert.equal(ui.spinner.hidden, true);
    assert.equal(ui.status.textContent, '');
    assert.equal(ui.fileArea.inert, false);
    assert.equal(ui.template.getAttribute('href'), '/staff/import?handler=Template');
    assert.equal(ui.template.getAttribute('tabindex'), null);
    assert.equal(ui.template.getAttribute('aria-disabled'), null);
    assert.equal(ui.recovery.hidden, true);
    assert.equal(fire(ui.template, 'click'), true);
    assert.equal(fire(ui.template, 'keydown', 'Enter'), true);
    assert.equal(fire(ui.form, 'drop'), true);
    assert.equal(fire(ui.form, 'submit'), true);
});

test('reset preserves pre-existing link attributes', () => {
    const attributes = { href: '/staff/import?handler=Template', tabindex: '0', 'aria-disabled': 'false' };
    const ui = setup(attributes);
    fire(ui.form, 'submit');
    fire(ui.window, 'pageshow');
    for (const [name, value] of Object.entries(attributes)) assert.equal(ui.template.getAttribute(name), value);
});

test('a prevented submission leaves the form usable', () => {
    const ui = setup();
    const event = new Event('submit', { cancelable: true });
    event.preventDefault();
    ui.form.dispatchEvent(event);
    assert.equal(ui.button.disabled, false);
    assert.equal(ui.fileArea.inert, false);
    assert.equal(ui.recovery.hidden, true);
    assert.equal(ui.template.getAttribute('href'), '/staff/import?handler=Template');
});
