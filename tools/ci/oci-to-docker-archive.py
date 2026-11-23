#!/usr/bin/env python3
"""Converte un'immagine esportata da buildx (--output type=docker, formato OCI con manifest.json)
nel formato classico di `docker save` (Docker fino alla 24): una cartella per strato con VERSION,
json e layer.tar non compresso, più manifest.json e repositories.

Serve a Container Station di QNAP, che rifiuta il formato OCI con «Invalid File Format».
docker load legge entrambi i formati.

Uso: oci-to-docker-archive.py <ingresso.tar[.gz]> <uscita.tar>
Solo libreria standard.
"""
import gzip
import hashlib
import io
import json
import os
import shutil
import sys
import tarfile
import tempfile

CHUNK = 1024 * 1024


def read_member(src, name):
    f = src.extractfile(name)
    if f is None:
        raise SystemExit(f"manca {name} nell'archivio")
    return f.read()


def blob_name(digest):
    algo, hexd = digest.split(":", 1)
    return f"blobs/{algo}/{hexd}"


def add_bytes(out, name, data):
    ti = tarfile.TarInfo(name)
    ti.size = len(data)
    ti.mode = 0o644
    ti.mtime = 0
    out.addfile(ti, io.BytesIO(data))


def add_dir(out, name):
    ti = tarfile.TarInfo(name)
    ti.type = tarfile.DIRTYPE
    ti.mode = 0o755
    ti.mtime = 0
    out.addfile(ti)


def main():
    if len(sys.argv) != 3:
        raise SystemExit(__doc__)
    src_path, dst_path = sys.argv[1], sys.argv[2]

    with tarfile.open(src_path, "r:*") as src:
        names = set(src.getnames())
        if "index.json" not in names:
            raise SystemExit("non è un archivio OCI (manca index.json)")
        index = json.loads(read_member(src, "index.json"))
        if len(index["manifests"]) != 1:
            raise SystemExit("serve un'immagine con una sola architettura")
        desc = index["manifests"][0]
        tag = desc.get("annotations", {}).get("io.containerd.image.name", "")
        legacy = json.loads(read_member(src, "manifest.json")) if "manifest.json" in names else []
        repo_tags = legacy[0].get("RepoTags", []) if legacy else []
        if not repo_tags and tag:
            # docker.io/library/nome:tag -> nome:tag
            short = tag.split("/")[-1]
            repo_tags = [short]
        if not repo_tags:
            raise SystemExit("nessun nome per l'immagine (RepoTags)")

        manifest = json.loads(read_member(src, blob_name(desc["digest"])))
        config_raw = read_member(src, blob_name(manifest["config"]["digest"]))
        config = json.loads(config_raw)
        diff_ids = config["rootfs"]["diff_ids"]
        layers = manifest["layers"]
        if len(layers) != len(diff_ids):
            raise SystemExit("numero di strati diverso da rootfs.diff_ids")

        config_hex = hashlib.sha256(config_raw).hexdigest()
        tmpdir = tempfile.mkdtemp(prefix="oci2docker-")
        try:
            with tarfile.open(dst_path, "w", format=tarfile.USTAR_FORMAT) as out:
                parent = ""
                layer_paths = []
                for i, (layer, diff_id) in enumerate(zip(layers, diff_ids)):
                    mt = layer["mediaType"]
                    raw = src.extractfile(blob_name(layer["digest"]))
                    if mt.endswith("tar+gzip") or mt.endswith(".tar.gzip"):
                        stream = gzip.GzipFile(fileobj=raw)
                    elif mt.endswith(".tar") or mt.endswith("image.rootfs.diff.tar"):
                        stream = raw
                    else:
                        raise SystemExit(f"tipo di strato non gestito: {mt}")
                    tmp = os.path.join(tmpdir, f"layer{i}.tar")
                    h = hashlib.sha256()
                    with open(tmp, "wb") as t:
                        while True:
                            b = stream.read(CHUNK)
                            if not b:
                                break
                            h.update(b)
                            t.write(b)
                    if "sha256:" + h.hexdigest() != diff_id:
                        raise SystemExit(f"strato {i}: diff_id non corrisponde")

                    layer_id = hashlib.sha256(f"{parent} {diff_id}".encode()).hexdigest()
                    v1 = {"id": layer_id, "created": config.get("created", "1970-01-01T00:00:00Z")}
                    if parent:
                        v1["parent"] = parent
                    if i == len(layers) - 1:
                        # lo strato in cima porta la configurazione, come faceva docker save
                        for k in ("architecture", "os", "variant", "config", "author"):
                            if k in config:
                                v1[k] = config[k]
                    else:
                        v1["container_config"] = {"Cmd": None}

                    add_dir(out, f"{layer_id}/")
                    add_bytes(out, f"{layer_id}/VERSION", b"1.0")
                    add_bytes(out, f"{layer_id}/json", json.dumps(v1, separators=(",", ":")).encode())
                    out.add(tmp, arcname=f"{layer_id}/layer.tar",
                            filter=lambda ti: (setattr(ti, "mtime", 0), setattr(ti, "uid", 0),
                                               setattr(ti, "gid", 0), setattr(ti, "uname", ""),
                                               setattr(ti, "gname", ""), setattr(ti, "mode", 0o644), ti)[-1])
                    os.remove(tmp)
                    layer_paths.append(f"{layer_id}/layer.tar")
                    parent = layer_id

                add_bytes(out, f"{config_hex}.json", config_raw)
                add_bytes(out, "manifest.json", json.dumps(
                    [{"Config": f"{config_hex}.json", "RepoTags": repo_tags, "Layers": layer_paths}],
                    separators=(",", ":")).encode())
                repos = {}
                for rt in repo_tags:
                    name, _, t = rt.rpartition(":")
                    repos.setdefault(name, {})[t] = parent
                add_bytes(out, "repositories", json.dumps(repos, separators=(",", ":")).encode())
        finally:
            shutil.rmtree(tmpdir, ignore_errors=True)

    print(f"{dst_path}: {len(layers)} strati, {', '.join(repo_tags)}")


if __name__ == "__main__":
    main()
