import { readdirSync } from 'node:fs';
import path from 'node:path';

export const HOSTS_FOLDER = 'backend/Hosts';
const SAFE_NAME = /^[A-Za-z0-9][A-Za-z0-9._-]*$/;

export function featureFolders(root) {
  return readdirSync(path.join(root, 'backend/Features'), { withFileTypes: true })
    .filter((entry) => entry.isDirectory())
    .map((entry) => entry.name)
    .sort();
}

export function discoverHosts(root) {
  const projects = [];
  const problems = [];
  const base = path.join(root, HOSTS_FOLDER);
  const backend = readdirSync(path.join(root, 'backend'), { withFileTypes: true });
  for (const misspelled of backend.filter((entry) => entry.name.toLowerCase() === 'hosts' && entry.name !== 'Hosts')) {
    problems.push(`backend/${misspelled.name}: la carpeta de hosts se escribe ${HOSTS_FOLDER}; con otra grafía, check-host-items y check-codegen no la recorren`);
  }
  if (!backend.some((entry) => entry.name === 'Hosts' && entry.isDirectory())) {
    problems.push(`${HOSTS_FOLDER} no existe; el gate no tiene qué revisar`);
    return { projects, problems };
  }
  const entries = readdirSync(base, { withFileTypes: true });
  for (const loose of entries.filter((entry) => entry.isFile() && entry.name.endsWith('.csproj')).map((entry) => entry.name).sort()) {
    problems.push(`${HOSTS_FOLDER}/${loose} está suelto; cada host vive en ${HOSTS_FOLDER}/<Host>/, con su .csproj en la raíz de esa carpeta`);
  }
  for (const link of entries.filter((entry) => entry.isSymbolicLink()).map((entry) => entry.name).sort()) {
    problems.push(`${HOSTS_FOLDER}/${link} es un enlace; un host es una carpeta real dentro de ${HOSTS_FOLDER}, y un enlace dejaría sus archivos fuera de los controles`);
  }
  const folders = entries
    .filter((entry) => entry.isDirectory())
    .map((entry) => entry.name)
    .sort();
  for (const folder of folders) {
    if (!SAFE_NAME.test(folder)) {
      problems.push(`${HOSTS_FOLDER}/${folder}: el nombre de un host lleva solo letras, dígitos, punto, guion o guion bajo`);
      continue;
    }
    const csprojs = readdirSync(path.join(base, folder), { withFileTypes: true })
      .filter((entry) => entry.isFile() && entry.name.endsWith('.csproj'))
      .map((entry) => entry.name);
    if (csprojs.length !== 1 || !SAFE_NAME.test(csprojs[0])) {
      problems.push(`${HOSTS_FOLDER}/${folder} tiene ${csprojs.length === 1 ? `un .csproj con un nombre que no se puede pasar a dotnet (${csprojs[0]})` : `${csprojs.length} .csproj`}; cada host tiene exactamente uno en la raíz de su carpeta`);
      continue;
    }
    projects.push(`${HOSTS_FOLDER}/${folder}/${csprojs[0]}`);
  }
  if (projects.length === 0 && problems.length === 0) problems.push(`${HOSTS_FOLDER} no tiene hosts; el gate no tiene qué revisar`);
  return { projects, problems };
}
