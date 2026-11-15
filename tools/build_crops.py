"""Veggie crop identities and provisional per-plant CEA gameplay parameters."""
from copy import deepcopy
from data_common import CONFIG, OUTPUT, cli, write


def build():
    return deepcopy(CONFIG['crops'])


if __name__ == '__main__':
    args = cli(__doc__).parse_args()
    write(args.output or OUTPUT / 'crop_parameters.json', build())
