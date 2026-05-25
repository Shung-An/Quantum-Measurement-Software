from __future__ import annotations

import importlib.util
import sys
from pathlib import Path


POST_PROCESSING_BROWSER = (
    Path(__file__).resolve().parents[1]
    / "prototype and postprocessing"
    / "post processing"
    / "datafiles_browser.py"
)

spec = importlib.util.spec_from_file_location("post_processing_datafiles_browser", POST_PROCESSING_BROWSER)
if spec is None or spec.loader is None:
    raise RuntimeError(f"Could not load {POST_PROCESSING_BROWSER}")

sys.path.insert(0, str(POST_PROCESSING_BROWSER.parent))
module = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = module
spec.loader.exec_module(module)


def main() -> None:
    module.main()


if __name__ == "__main__":
    main()
