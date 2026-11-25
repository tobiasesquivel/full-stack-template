// Componente de quiz del curso.
// Marcado: <main data-lesson="Lección 1: el recorrido del pedido"> ...
//   <div class="quiz" data-answer="1" data-id="L1-P2"> <p class="q">…</p>
//     <div class="opts"><button>…</button>…</div> <p class="why">explicación</p> </div>
//   <div class="quiz-result"></div>
// data-answer: índice (desde 0) del botón correcto. data-id: nombre de la pregunta en la línea de resultado.
// Al responder todo, .quiz-result muestra una línea para copiar y pegar en el chat.
(function () {
  const style = document.createElement('style');
  style.textContent = `
    .quiz { border: 1px solid var(--c-line); border-radius: var(--c-radius); padding: .9rem 1rem; margin: 1rem 0; font-family: var(--c-sans); }
    .quiz .tipo { font-size: .7rem; font-weight: 600; letter-spacing: .05em; text-transform: uppercase; color: var(--c-muted); margin: 0 0 .3rem; }
    .quiz .q { font-weight: 600; margin: 0 0 .6rem; }
    .quiz pre { margin: .2rem 0 .7rem; }
    .quiz .opts { display: grid; gap: .4rem; }
    .quiz button { font: inherit; font-size: .9rem; text-align: left; padding: .5rem .75rem; border-radius: var(--c-radius); border: 1px solid var(--c-line); background: transparent; color: inherit; cursor: pointer; }
    .quiz button:hover:not(:disabled) { border-color: var(--c-accent); }
    .quiz button:disabled { cursor: default; }
    .quiz button.ok { border-color: var(--c-ok); background: color-mix(in srgb, var(--c-ok) 14%, transparent); }
    .quiz button.ko { border-color: var(--c-ko); background: color-mix(in srgb, var(--c-ko) 14%, transparent); }
    .quiz .why { display: none; font-size: .9rem; margin: .6rem 0 0; }
    .quiz.done .why { display: block; }
    .quiz-result { font-family: var(--c-sans); font-size: .9rem; background: var(--c-card); border-radius: var(--c-radius); padding: .9rem 1rem; margin: 1rem 0; }
    .quiz-result .line { display: flex; gap: .5rem; align-items: center; margin-top: .5rem; }
    .quiz-result code { flex: 1; padding: .4rem .6rem; }
    .quiz-result button { font: inherit; padding: .4rem .8rem; border-radius: var(--c-radius); border: 1px solid var(--c-accent); background: transparent; color: var(--c-accent); cursor: pointer; }
  `;
  document.head.appendChild(style);

  const lesson = document.querySelector('main')?.dataset.lesson ?? document.title;
  const quizzes = [...document.querySelectorAll('.quiz')];
  const result = document.querySelector('.quiz-result');
  const failed = [];
  let answered = 0;

  const update = () => {
    if (!result) return;
    if (answered < quizzes.length) {
      result.textContent = `Respondidas: ${answered} de ${quizzes.length}. Al terminar aparece la línea para pegarme en el chat.`;
      return;
    }
    const right = quizzes.length - failed.length;
    const line = `${lesson}: ${right} de ${quizzes.length}. ` + (failed.length ? `Fallé: ${failed.join(', ')}.` : 'Sin fallas.');
    result.innerHTML = '<strong>Copiá esta línea y pegámela en el chat:</strong><div class="line"><code></code><button type="button">Copiar</button></div>';
    result.querySelector('code').textContent = line;
    const copy = result.querySelector('button');
    copy.addEventListener('click', async () => {
      try { await navigator.clipboard.writeText(line); copy.textContent = 'Copiada'; }
      catch { copy.textContent = 'Copiala a mano'; }
    });
  };

  quizzes.forEach((quiz, n) => {
    const correct = Number(quiz.dataset.answer);
    const id = quiz.dataset.id ?? `P${n + 1}`;
    const buttons = [...quiz.querySelectorAll('.opts button')];
    buttons.forEach((button, i) => button.addEventListener('click', () => {
      buttons.forEach((b) => { b.disabled = true; });
      buttons[correct].classList.add('ok');
      if (i !== correct) { button.classList.add('ko'); failed.push(id); }
      quiz.classList.add('done');
      answered++;
      update();
    }));
  });
  update();
})();
