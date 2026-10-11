(function () {
  'use strict';

  // ---- mobile menu
  var toggle = document.querySelector('[data-nav-toggle]');
  var nav = document.getElementById('mainNav');
  if (toggle && nav) {
    toggle.addEventListener('click', function () { nav.classList.toggle('open'); });
  }

  // ---- "Are you sure?" on buttons/forms that carry data-confirm
  document.addEventListener('submit', function (e) {
    var form = e.target;
    var msg = form.getAttribute && form.getAttribute('data-confirm');
    if (msg && !window.confirm(msg)) { e.preventDefault(); }
  });

  // ---- keep check-out after check-in on the date pickers
  var ci = document.querySelector('[data-checkin]');
  var co = document.querySelector('[data-checkout]');
  function plusDay(value) {
    var d = new Date(value + 'T00:00:00');
    d.setDate(d.getDate() + 1);
    var m = String(d.getMonth() + 1).padStart(2, '0');
    var day = String(d.getDate()).padStart(2, '0');
    return d.getFullYear() + '-' + m + '-' + day;
  }
  if (ci && co) {
    var sync = function () {
      if (!ci.value) return;
      var min = plusDay(ci.value);
      co.min = min;
      if (!co.value || co.value < min) co.value = min;
    };
    ci.addEventListener('change', sync);
    sync();
  }

  // ---- room gallery
  var main = document.querySelector('[data-gallery-main]');
  var thumbs = document.querySelectorAll('[data-gallery-thumb]');
  thumbs.forEach(function (btn) {
    btn.addEventListener('click', function () {
      if (main) main.src = btn.getAttribute('data-gallery-thumb');
      thumbs.forEach(function (b) { b.classList.remove('active'); });
      btn.classList.add('active');
    });
  });

  // ---- payment page
  var payForm = document.querySelector('[data-pay-form]');
  if (payForm) {
    var radios = payForm.querySelectorAll('input[name="method"]');
    var panels = payForm.querySelectorAll('[data-panel]');
    var show = function () {
      var chosen = payForm.querySelector('input[name="method"]:checked');
      var value = chosen ? chosen.value : '';
      var kind = value === 'Credit/Debit Card' ? 'card'
               : value === 'Bank Transfer' ? 'bank'
               : value ? 'mobile' : '';
      panels.forEach(function (p) {
        p.classList.toggle('show', p.getAttribute('data-panel') === kind);
      });
      var label = payForm.querySelector('[data-mobile-label]');
      if (label) label.textContent = value || 'Mobile banking';
    };
    radios.forEach(function (r) { r.addEventListener('change', show); });
    show();

    // card number: groups of 4
    var card = payForm.querySelector('[data-card-number]');
    if (card) {
      card.addEventListener('input', function () {
        var digits = card.value.replace(/\D/g, '').slice(0, 16);
        card.value = digits.replace(/(.{4})/g, '$1 ').trim();
      });
    }

    // simulated OTP
    var otpBtn = payForm.querySelector('[data-send-otp]');
    var otpMsg = payForm.querySelector('[data-otp-msg]');
    var otpField = payForm.querySelector('[data-otp-field]');
    if (otpBtn) {
      otpBtn.addEventListener('click', function () {
        var data = new FormData();
        data.append('id', payForm.getAttribute('data-booking-id'));
        data.append('mobile', (payForm.querySelector('[name="mobile"]') || {}).value || '');
        data.append('__RequestVerificationToken', (payForm.querySelector('input[name="__RequestVerificationToken"]') || {}).value || '');
        otpBtn.disabled = true;
        fetch(otpBtn.getAttribute('data-send-otp'), { method: 'POST', body: data, credentials: 'same-origin' })
          .then(function (r) { return r.json(); })
          .then(function (j) {
            otpMsg.textContent = j.message || '';
            otpMsg.style.color = j.ok ? '#1f7a4d' : '#a12b2b';
            if (j.ok && j.otp && otpField) otpField.value = j.otp;
          })
          .catch(function () { otpMsg.textContent = 'Could not send OTP. Please try again.'; otpMsg.style.color = '#a12b2b'; })
          .finally(function () { otpBtn.disabled = false; });
      });
    }
  }

  // ---- facility booking: live total
  var fac = document.querySelector('[data-facility]');
  if (fac) {
    var price = parseFloat(fac.getAttribute('data-price'));
    var units = fac.querySelector('[name="units"]');
    var out = fac.querySelector('[data-total]');
    var upd = function () {
      var n = parseInt(units.value, 10) || 0;
      out.textContent = (price * n).toLocaleString('en-US') + ' BDT';
    };
    units.addEventListener('input', upd);
    upd();
  }
})();
